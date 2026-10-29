# ADR-006 — Fila de execução: Web → Redis → Worker

Status: Proposed
Data: 2026-10-28

## Contexto

O ADR-004 decidiu executar o código do aluno em containers Docker descartáveis e apontou o maior risco:
**quem fala com `/var/run/docker.sock` controla o host**. Na PoC (#13) a própria Web chamava o Docker,
o que só servia para desenvolvimento. A issue #25 expõe a execução por HTTP, então a Web não pode mais ter
esse poder: se ela for invadida, o invasor não pode ganhar o host.

Restrições: stack do ADR-001 (o Redis já está no `docker-compose.yml`), equipe pequena, resposta
síncrona para a tela (o aluno clica em "Executar" e espera alguns segundos), e o mesmo banco de dados
para gravar a `CodeSubmission`.

## Decisão

1. **Dois processos.** `DevPath.Web` recebe o pedido, confere login, CSRF, limite e se a aula está
   liberada, e **só enfileira**. `DevPath.Worker` (projeto novo, `src/DevPath.Worker`) é o **único
   processo** que registra `ICodeSandbox`/`ICodeRunner`/`IExerciseSubmissionService` e o único container do
   compose que monta o socket do Docker. Um teste de integração garante que o contêiner de DI da Web não
   tem nenhum desses serviços.
2. **Fila = LIST do Redis** (StackExchange.Redis), não Stream:
   - Web: `SUBSCRIBE <fila>:pronto:<job>` → `LPUSH <fila> <job json>` → `PUBLISH <fila>:novo` → espera o
     aviso (ou o tempo-limite) → `GET <fila>:resultado:<job>`.
   - Worker: `Sandbox:MaxConcurrency` laços fazendo `RPOP <fila>` (FIFO); com a fila vazia, dorme até o
     aviso em `<fila>:novo` ou até `Execution:PollIntervalMilliseconds`. Executa, grava a
     `CodeSubmission`, faz `SET <fila>:resultado:<job>` com TTL (`Execution:ResultTtlSeconds`) e
     `PUBLISH <fila>:pronto:<job>`.
   - Por que não `BRPOP`/`BLPOP`: o `ConnectionMultiplexer` compartilha uma conexão e não aceita comandos
     bloqueantes; Pub/Sub dá o mesmo "acordar na hora" sem bloquear.
   - **Por que LIST e não STREAM**: o pedido é **síncrono e descartável**. A Web espera
     `Execution:ResultTimeoutSeconds` (15 s, um pouco acima dos 10 s do sandbox) e depois desiste com 503;
     o aluno clica de novo. O que o Stream oferece a mais (consumer groups, `XACK`, lista de pendentes,
     `XCLAIM`/reentrega) serve para **não perder** trabalho, mas aqui reentregar um job depois que a Web
     desistiu seria pior: gastaria um container e gravaria uma tentativa (inclusive contando para
     `MaxAttempts`) que o aluno nunca viu. Por isso a garantia é **no máximo uma vez**: se o Worker cair no
     meio de um job, o job se perde e a Web responde 503. Uma LIST é um comando de cada lado, fácil de
     inspecionar (`LLEN`, `LRANGE`) e de explicar. Se um dia a execução virar assíncrona (correção em lote,
     outras linguagens lentas), trocamos por Stream com consumer group: só `RedisExecutionQueue` e
     `ExecutionWorker` mudam.
3. **Prazos.** O job leva `DeadlineUtc` (agora + `ResultTimeoutSeconds`). O Worker descarta job vencido sem
   executar e cancela a execução 1 s antes do prazo (`ExecutionWorker.ReplyMargin`), respondendo
   `timeout`; assim o "demorou demais" chega ao aluno em vez de um 503. Os relógios da Web e do Worker
   precisam estar sincronizados (NTP), como em qualquer deploy.
4. **Regra de acesso** na Web, atrás de `ILessonAccessPolicy` (Application): a aula do exercício precisa
   estar publicada (`IsPublished`), não ser rascunho (`IsDraft`), com o módulo publicado e a trilha ativa.
   Inexistente, escondido ou rascunho = **o mesmo 404 com a mesma mensagem** (não revela qual é o caso).
   TODO: liberar por progresso, junto com "marcar como concluída" (#27).
5. **Rate limiting** na Web (`Microsoft.AspNetCore.RateLimiting`), por usuário (claim `NameIdentifier`):
   janela deslizante de `RateLimiting:Execution:PermitLimit` (10) execuções por `WindowSeconds` (60), em
   `SegmentsPerWindow` (6) pedaços, **e** `ConcurrentPerUser` (1) execução por vez, sem fila. Estourou:
   429 com `Retry-After` e JSON em português: pela janela, mensagem "Calma! Você enviou muitas execuções
   seguidas..." e `Retry-After` = um segmento (60 s / 6 = 10 s); pela concorrência, "Sua execução anterior
   ainda está rodando..." e `Retry-After` = `ConcurrencyRetryAfterSeconds` (2). O limite é **em memória, por instância da Web**: com mais de uma instância, cada uma conta o seu e o
   aluno ganha N vezes o limite. Aceitável no MVP (uma instância só); **um limiter no Redis é obrigatório
   antes de rodar mais de uma instância da Web**: [#28](https://github.com/ZoroarkDEV/Plataform_Education/issues/28)
   (Fase 2).
6. **Contrato para o Frontend** (abaixo) é o mesmo objeto que o Worker grava no Redis
   (`ExecutionOutcome`, JSON camelCase).

### Contrato HTTP

`POST /api/exercicios/{exerciseId}/executar` — exige login (cookie do Identity) e token antiforgery.

- Header antiforgery: `RequestVerificationToken: <token>`. O token vem de
  `GET /api/antiforgery/token` (logado) → `{ "token": "...", "headerName": "RequestVerificationToken" }`,
  ou do `<input name="__RequestVerificationToken">` que o Razor já coloca nos `<form>`. O cookie
  `.AspNetCore.Antiforgery.*` vai junto sozinho (mesma origem). O token é ligado ao usuário: busque de
  novo depois de login/logout.
- Corpo: `{ "code": "SELECT titulo, preco FROM livros ORDER BY preco DESC;" }` (até 20.000 caracteres).

Respostas:

| Status | Quando | Corpo |
|---|---|---|
| 200 | Pedido processado: passou, falhou, erro de SQL, timeout ou tentativas esgotadas (ver `status`) | `ExecutionOutcome` |
| 400 | Código vazio/grande demais, ou token antiforgery ausente/inválido | `{ "message": "..." }` |
| 401 | Sem login (em `/api` não há redirect para a tela de login) | vazio |
| 404 | Exercício inexistente, escondido ou de aula em rascunho (mesma mensagem nos três) | `{ "message": "Exercício não encontrado." }` |
| 429 | Rate limit por aluno (só isso; tentativas esgotadas é 200) | `{ "message": "...", "retryAfterSeconds": 10 }` + header `Retry-After` |
| 503 | Worker/Redis não respondeu a tempo | `{ "message": "..." }` |

`ExecutionOutcome`:

```json
{
  "status": "fail",
  "columns": ["titulo", "preco"],
  "rows": [["Dom Casmurro", "29.90"], ["Iracema", null]],
  "truncated": false,
  "friendlyMessage": "Sua consulta rodou, mas o resultado ainda não é o esperado. Veja os casos de teste.",
  "technicalDetail": null,
  "executionTimeMs": 1830,
  "passedCases": 1,
  "totalCases": 2,
  "testCases": [
    { "name": "livros caros", "passed": true, "message": null },
    { "name": "com promoção", "passed": false, "message": "Sua consulta devolveu 3 linhas, mas o esperado é 2." }
  ],
  "submissionId": "6f1c2a8e-3b7d-4c55-9a0e-2f4b8d1e7c90",
  "remainingAttempts": null
}
```

- `status`: `pass` | `fail` | `error` | `timeout` | `attempts_exhausted`.
  - `attempts_exhausted`: o aluno já usou todas as tentativas (`MaxAttempts`). Nada executa nem é gravado;
    `friendlyMessage` explica em português ("Você já usou as 3 tentativas deste exercício. Revise a aula..."),
    `remainingAttempts` é `0`, `submissionId` é `null`. Vale também para quem perde a corrida de dois envios
    simultâneos (ver Consequências).
- `columns`/`rows`: o resultado **do aluno** no primeiro caso de teste; cada célula é o texto que o
  PostgreSQL devolveu (`null` = NULL). No máximo 200 linhas; `truncated: true` quando havia mais.
  Vazios em `error`/`timeout`/`attempts_exhausted`.
- `friendlyMessage`: sempre preenchida, para iniciante. `technicalDetail`: a mensagem original do
  PostgreSQL (com SQLSTATE), para o "Ver detalhe técnico" recolhido; `null` quando não há.
- `testCases`: um item por caso, na ordem. **Nunca** traz as linhas esperadas nem a solução; casos que não
  rodaram (porque um anterior deu erro/timeout) vêm com `passed: false` e uma mensagem dizendo isso.
- `submissionId`: `null` quando nada foi gravado (tentativas esgotadas, erro de configuração do exercício).
- `remainingAttempts`: `null` = sem limite.

Erro na preparação do próprio exercício (dataset ou `setup` do caso quebrado no sandbox): `status: "error"`
com uma frase neutra ("problema de configuração, não é culpa sua"), **não conta tentativa** e nada é gravado.
O Worker registra um log `Error` começando com **"Erro de conteúdo"**, com `exerciseId`, slug da aula,
dataset, caso de teste e o detalhe do PostgreSQL, para a equipe achar e corrigir o conteúdo. Casos de teste
ilegíveis e dataset inexistente geram o mesmo marcador.

**Exercícios do seed (até o #24):** os `*.exercise.json` em `content/` ainda trazem os casos de teste em
snake_case (`test_cases`, `order_matters`, `check_column_names`), formato que o `TestCaseSpec` do #13 recusa. Até o #24 converter o conteúdo, executar
um exercício do seed devolve `status: "error"` (configuração do exercício) e o log "Erro de conteúdo".

Exemplo de erro (DDL/DML): `{ "status": "error", "friendlyMessage": "Neste exercício só é permitido
consultar dados com SELECT. ...", "technicalDetail": "ERROR:  42P11: cannot open DELETE query as cursor ...", ... }`.

## Consequências

Mais fácil:
- A Web perde o poder de criar containers: invadir a Web não dá o host. O socket fica só no Worker, que não
  abre porta nem recebe HTTP.
- O Worker escala sozinho (mais réplicas = mais laços consumindo a mesma LIST) e pode ir para uma máquina/VM
  sem dados, com Docker rootless ou gVisor (ADR-004), sem mudar a Web.
- Redis fora do ar só desliga a execução (503); o resto do site continua (a conexão é criada no primeiro uso,
  com `AbortOnConnectFail = false`).

Mais difícil / em aberto:
- Mais um processo para subir e monitorar (`docker compose up worker`). Sem Worker, toda execução vira 503
  depois de 15 s.
- O Worker ainda é **root no host** pelo socket (ADR-004). Produção pede host dedicado, rootless ou um
  proxy do socket que só permita as chamadas do sandbox.
- `MaxAttempts` é conferido duas vezes no Worker: antes de executar (para não gastar container) e na hora de
  gravar, dentro de uma transação com `pg_advisory_xact_lock` por (aluno, exercício). Dois envios
  simultâneos (duas instâncias da Web, vários Workers) não passam do limite: o segundo executa, mas recebe
  `status: "attempts_exhausted"` (200, igual ao da pré-checagem) e nada é gravado. O lock dura só a transação curta da gravação.
- Rate limit em memória (por instância da Web), como dito acima: um limiter no Redis é pré-requisito para
  escalar a Web horizontalmente: [#28](https://github.com/ZoroarkDEV/Plataform_Education/issues/28) (Fase 2).
