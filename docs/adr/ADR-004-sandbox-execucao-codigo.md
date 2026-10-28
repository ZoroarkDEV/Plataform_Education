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
(`initdb` + `pg_ctl`, só socket Unix), cria o papel `aluno` **sem superusuário**, roda a preparação do
caso de teste e depois o código do aluno com `psql -A` (saída `coluna|coluna`, sem rodapé). Por não ser
superusuário, `COPY ... TO PROGRAM` é negado. `ExerciseSubmissionService` roda cada caso de teste, compara
a saída com `OutputComparer` (ignora `\r\n`, espaços no fim e linhas vazias nas pontas) e grava uma
`CodeSubmission`.

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
- O `psql` permite meta-comandos como `\!` (shell); eles rodam **dentro** do container, já isolado
  (sem rede, sem root, só leitura), então o risco fica contido, mas o aluno pode ver o sistema de arquivos da imagem.
- Ainda não há endpoint HTTP nem limite de envios por usuário (rate limit); entram junto com a tela
  de exercícios, de preferência já no worker separado.
