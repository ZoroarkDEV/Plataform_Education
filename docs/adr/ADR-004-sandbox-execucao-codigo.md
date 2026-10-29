# ADR-004 — Sandbox de execução de código do aluno

Status: Proposed
Data: 2026-10-28

## Contexto

Na Fase 3 (issue #13) o aluno envia código (começando por SQL) e o DevPath corrige rodando esse código
contra casos de teste. Código de aluno é, por definição, **código não confiável**: pode ser um loop
infinito, um `fork` em cascata, um consumo de memória sem fim, uma tentativa de ler arquivos do servidor
ou de acessar a rede interna. Um erro aqui compromete o servidor inteiro.

Restrições:

- Stack oficial (ADR-001): .NET 9, PostgreSQL, Docker. Nada de serviço pago no MVP.
- O aluno precisa aprender o **dialeto real** (PostgreSQL), não uma imitação.
- Timeout de 5 a 10 s por execução (SPEC §8) e resposta rápida o bastante para uma tela interativa.
- Equipe pequena: a solução precisa ser entendida e operada por quem está aprendendo.

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| **Roslyn / execução no próprio processo** | Muito rápido, zero infraestrutura | Não há isolamento real em .NET moderno (AppDomain/CAS não existem mais): um `while(true)` ou `File.Delete` roda com os poderes da aplicação. Não serve para SQL. **Descartada.** |
| **Container Docker efêmero por execução** | Isolamento do kernel (namespaces + cgroups), limites de CPU/memória/processos nativos, mesma imagem do PostgreSQL que usamos, já está na stack | ~1,5 s por execução de SQL (subir o Postgres); a aplicação precisa falar com o daemon Docker (ver riscos) |
| **Judge0 (self-hosted ou API)** | Pronto, muitas linguagens, fila embutida | Mais um serviço para operar (vários containers, Redis, Postgres próprio); a versão hospedada é paga e envia código do aluno para terceiros; pouco controle sobre a versão do PostgreSQL |
| **gVisor / Firecracker** | Isolamento mais forte (kernel em espaço de usuário / microVM) | Exige KVM ou runtime especial no host, configuração bem mais complexa; exagero para a PoC. **Fica como evolução**: o gVisor entra como runtime do próprio Docker (`--runtime=runsc`) sem mudar o código |

## Decisão

Usar **um container Docker descartável por execução**, criado pela interface `ICodeSandbox`
(Application) e implementado por `DockerCliSandbox` (Infrastructure). Código de aluno **nunca** roda
no processo da aplicação.

Cada execução roda `docker run` com:

| Flag | Para quê |
|---|---|
| `--rm`, nome único `devpath-sandbox-<guid>`, label `devpath.sandbox=true` | Container some ao terminar; dá para matar/achar exatamente aquele |
| `--network none` | Sem rede (só loopback) |
| `--memory 256m --memory-swap 256m` | Limite de RAM sem swap extra; estourou, o kernel mata (exit 137) |
| `--cpus 1` | No máximo um núcleo |
| `--pids-limit 64` | Barra fork bomb |
| `--read-only` + `--tmpfs /tmp:rw,noexec,nosuid,nodev,size=64m` | Imagem só leitura; único lugar gravável é um /tmp em memória onde nada executa |
| `--user 65534:65534` | Usuário `nobody`, nunca root |
| `--cap-drop ALL`, `--security-opt no-new-privileges` | Nenhuma capability; setuid não dá privilégio |
| `--init`, `--pull never`, `--log-driver none` | Repasse de sinais; nunca baixa imagem durante uma execução; sem cópia da saída em disco |
| (sem `-p`, sem `-v`, sem `--privileged`) | Nenhuma porta publicada, nenhum volume do host |

Além das flags, a aplicação aplica:

- **Timeout de parede** (padrão 10 s, incluindo subir o container): estourou, `docker kill <nome>` e o
  resultado volta com `TimedOut = true`. Cancelamento de quem chamou também mata o container.
- **Limite de saída** (64 mil caracteres em stdout e em stderr): passou, corta e mata (`OutputTruncated`).
- **Fila**: no máximo `Sandbox:MaxConcurrency` (padrão 4) execuções simultâneas (semáforo).
- **Lista de imagens permitidas** (`Sandbox:AllowedImages`, padrão `postgres:16-alpine`).
- **`docker rm -f` no `finally`**, como rede de segurança.
- Código do aluno entra **só pelo stdin**; os argumentos vão por `ProcessStartInfo.ArgumentList` (sem shell no host).
- Erro do Docker em si (exit 125: imagem ausente, daemon fora do ar) vira `SandboxUnavailableException`:
  falha da plataforma, não do aluno.

**SQL** (`PostgresSqlCodeRunner`): dentro do container sobe um PostgreSQL 16 temporário em /tmp
(`initdb` + `pg_ctl`, só socket Unix). O container é a fronteira **externa**; o banco dentro dele também
isola o aluno de verdade, porque o container sozinho não impede que o aluno vire superusuário **do
PostgreSQL** e use os poderes dele (rodar programa, ler arquivo, apagar o dataset). A revisão de QA da #13
mostrou exatamente isso na primeira versão (`trust` + o código do aluno lido pelo `psql`: `\c - postgres`
e `\!` funcionavam). Regras atuais:

1. **Senha em toda conexão, inclusive local** (`initdb --auth=scram-sha-256`, nada de `trust`).
   A senha do superusuário `postgres` é **aleatória por execução** (`/dev/urandom`), fica numa variável do
   shell que nunca é exportada, e o arquivo que o `initdb` lê é apagado logo depois. O `psql` do aluno roda
   só com a senha do aluno no ambiente.
2. **Papel `aluno` sem poder nenhum**: `LOGIN` com senha própria, `NOSUPERUSER NOCREATEDB NOCREATEROLE
   NOREPLICATION NOBYPASSRLS`, sem `pg_execute_server_program`, `pg_read_server_files` e
   `pg_write_server_files`; não é dono de nada; só `CONNECT`, `USAGE` nos schemas do exercício e `SELECT` nas
   tabelas. `default_transaction_read_only = on` e `statement_timeout` (3 s) ficam presos ao papel.
   O servidor também tem `temp_file_limit` e `work_mem`, para uma consulta pesada não encher o /tmp.
3. **O texto do aluno nunca é lido pelo `psql` como script.** O `psql` executa um script **fixo**:
   ```
   \set codigo `cat /tmp/aluno.sql`          -- o arquivo vira o VALOR da variável, sem ser interpretado
   SELECT devpath.validar_consulta(:'codigo'); -- :'codigo' = literal SQL escapado pelo próprio psql
   SELECT format('SELECT * FROM (%s) ... LIMIT 0', :'codigo') \gexec
   SELECT format('SELECT json_agg(...) FROM (SELECT * FROM (%s) ... LIMIT 201) ...', :'codigo') \gexec
   ```
   O `\gexec` manda o resultado do `format()` **direto ao servidor**; o `psql` não relê esse texto. Para o
   servidor, `\c`, `\!`, `\copy`, `\o`, `\i`, `\set`... são só texto que começa com `\`, ou seja,
   **erro de sintaxe (42601)**. Não existe caminho para meta-comando: o único texto que o `psql` interpreta é
   o nosso. Alternativas descartadas: filtrar `\` no C# (lista negra, frágil: `\` dentro de string é SQL
   válido) e escrever um cliente próprio no container (mais código para manter, mesmo resultado).
4. **Uma consulta só.** `devpath.validar_consulta` (PL/pgSQL, `SECURITY INVOKER`, ou seja, roda como o
   aluno) faz `OPEN cursor FOR EXECUTE codigo`: o PostgreSQL só abre cursor para **uma** instrução que
   devolve linhas (SELECT/WITH/VALUES/TABLE). Várias instruções, `COPY`, `SET ROLE`, `ALTER`, `DELETE`... são
   recusadas (42P11) antes de qualquer execução. Isso também impede o truque de "fechar" o
   `SELECT * FROM (` e injetar instruções, porque o código precisa ser válido sozinho.
5. **A garantia é o papel, não o validador.** Os itens 3 e 4 tornam o erro amigável e a saída previsível;
   quem impede o estrago é o item 2. Os testes provam os dois: tentativas por caminhos que passam pelo
   validador (`set_config('role', 'postgres')`, `pg_read_file`, `pg_authid`, `data_directory`) recebem
   42501 do próprio banco, e um teste lê de `pg_roles`/`pg_has_role` que o aluno não é superusuário nem
   membro dos papéis perigosos (`AlunoNaoEhSuperusuario_CopyToProgramEhNegado`).
6. **Nada secreto entra no container.** O container recebe só o código do aluno e a **preparação**
   (SQL do dataset + `setup` do caso de teste), que roda como superusuário antes de o aluno entrar. O
   **resultado esperado e a solução nunca vão para lá**: a comparação acontece no worker/aplicação
   (`ResultComparer`). Assim, mesmo uma fuga completa do banco não revela a resposta.

**Entrada** (stdin): `"<tamanho do código em bytes UTF-8>\n" + código + preparação`. O tamanho separa as
duas partes sem marcador que o aluno pudesse imitar, e nada do aluno vai em argumento (limite de tamanho,
aparece no `ps`). Ponto e vírgula e espaços no fim são removidos antes (`SELECT 1;` é o normal de escrever).

**Saída estruturada**: o cabeçalho vem em CSV (`LIMIT 0`: nomes na ordem, até com resultado vazio e nomes
repetidos) e os valores num único `json_agg` (tipo `json`, que preserva ordem e nomes repetidos). Por que
JSON: ele já diz o **tipo** de cada valor (número sem aspas, texto com aspas, `true`/`false`, `null`), que é
o que as regras de comparação precisam; texto puro confunde `NULL` com string vazia e `29.90` com `"29.90"`.
Até 200 linhas (busca 201 para saber se cortou: `Truncated`).

**Casos de teste** (`Exercise.TestCases`, jsonb, camelCase, formato combinado com Dev/PO):
`[{ name, dataset?, setup?, columns: string[], rows: (string|number|bool|null)[][], orderMatters = false,
checkColumnNames = false }]`. `dataset` é o slug da tabela `Datasets` (Id, Slug único, Sql, ContentHash),
semeada a partir de `content/datasets/*.sql`; dataset inexistente é erro de conteúdo
(`DatasetNotFoundException`), sem gastar container nem gravar tentativa. Regras do `ResultComparer`:
quantidade e ordem das colunas sempre contam; nomes só com `checkColumnNames`; ordem das linhas só com
`orderMatters` (senão multiconjunto); número pelo valor (29.9 == 29.90, inteiro vs decimal); `null` só com
`null`; texto e bool exatos. O feedback diz o que está diferente sem mostrar as linhas esperadas.
`ExerciseSubmissionService` roda cada caso, traduz erros do PostgreSQL pelo SQLSTATE
(`SqlErrorTranslator`) e grava uma `CodeSubmission`.

**Docker CLI em vez de Docker.DotNet**: as flags ficam escritas igual à documentação (fáceis de revisar e
de reproduzir no terminal), o stdin é trivial (`-i`) e não entra dependência nova. Custo: um processo a
mais por execução (milissegundos) e ler o código de saída em vez de objetos tipados.

## Consequências

Mais fácil:
- Isolamento forte com ferramentas que a equipe já usa; cada barreira tem um teste automatizado
  (`tests/DevPath.IntegrationTests/CodeExecution`, pulados quando não há Docker).
- Novas linguagens = nova imagem na lista + um `ICodeRunner`.
- Trocar para gVisor (`--runtime=runsc`) ou rootless Docker não muda o código da aplicação.

Mais difícil / riscos conhecidos:
- **Socket do Docker = root no host.** Quem consegue falar com `/var/run/docker.sock` consegue criar um
  container privilegiado. Se a aplicação web for invadida, o invasor ganha o host. Mitigações, em ordem:
  1) **worker separado**: o sandbox roda num serviço próprio (fila Redis), numa máquina/VM sem dados,
     e a Web nunca vê o socket; 2) **Docker rootless** nesse worker; 3) **gVisor** (`runsc`) como runtime;
     4) um proxy do socket que só permite `create/start/kill/rm` com as flags acima.
  **Na PoC, a Web fala direto com o Docker local: aceitável só em desenvolvimento.**
- **Latência**: ~1,5 s por caso de teste de SQL (subir o Postgres). Evolução: um pool de containers
  quentes ou um Postgres compartilhado com um schema/papel descartável por execução.
- **OOM é heurística**: exit 137 sem timeout e sem corte de saída é tratado como falta de memória;
  como o `--rm` apaga o container, não dá para confirmar com `docker inspect` (State.OOMKilled).
- **Janela de corrida**: se o processo `docker` morrer antes de o container ser criado, o `kill`/`rm`
  podem não achar nada e o container nascer logo depois. O timeout interno não existe nesse caso; o label
  `devpath.sandbox=true` permite uma limpeza periódica (`docker ps --filter label=devpath.sandbox`).
- **Superfície do PostgreSQL**: o aluno ainda roda SQL arbitrário (somente leitura) num servidor de verdade;
  uma falha no próprio PostgreSQL seria contida pelo container. Mantemos a imagem `postgres:16-alpine`
  atualizada e `--pull never` (a atualização é deliberada, no host).
- **Custo**: cada caso de teste sobe um PostgreSQL novo (~1,5 a 3 s); a consulta do aluno roda duas vezes
  (cabeçalho com `LIMIT 0` e valores), o que é barato porque o `LIMIT 0` não lê linhas.
- Ainda não há endpoint HTTP nem limite de envios por usuário (rate limit); entram junto com a tela
  de exercícios, de preferência já no worker separado.
