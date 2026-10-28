# Architecture Decision Records (ADRs)

Aqui ficam as decisões técnicas importantes do DevPath, cada uma num arquivo curto.
O objetivo é simples: daqui a alguns meses, qualquer pessoa (ou agente) deve conseguir entender
**o que** foi decidido, **por quê** e **o que isso custa**, sem precisar reconstruir a conversa.

## Quando criar um ADR

Seguindo o papel **DevPath Architect** (`DEVPATH-AGENTS-SKILLS.md`), crie um ADR sempre que:

- Escolher (ou mudar) uma biblioteca importante
- Definir a estratégia de execução de código de usuário
- Definir a estratégia de autenticação
- Introduzir um novo bounded context ou padrão

## Índice

| ADR | Título | Status |
|-----|--------|--------|
| [ADR-001](ADR-001-stack-oficial-do-mvp.md) | Stack oficial do MVP | Accepted |
| [ADR-002](ADR-002-monolito-modular-em-camadas.md) | Monólito modular em camadas | Accepted |
| [ADR-003](ADR-003-conteudo-pedagogico-em-content.md) | Conteúdo pedagógico em `/content` | Accepted |
| [ADR-005](ADR-005-enumeracao-de-contas.md) | Enumeração de contas (login e cadastro) | Proposed |

Decisões já previstas para ADRs futuros:
- Estratégia de execução (sandbox) de código do aluno — issue #13 (Fase 3).

## Convenções

- Nome do arquivo: `ADR-XXX-titulo-em-kebab-case.md`, com numeração sequencial (`001`, `002`, ...).
- Status possíveis: `Proposed` (aguardando aprovação), `Accepted` (em vigor) e `Deprecated` (não vale mais).
- Um ADR aceito **não é reescrito**: se a decisão mudar, crie um ADR novo e marque o antigo como `Deprecated`, apontando para o substituto.
- Em caso de conflito entre documentos, vale a ordem de autoridade do README: **README > SPEC > código**.
- Escreva em PT-BR, curto e direto. Atualize a tabela do índice acima no mesmo PR.

## Template (formato mínimo do DevPath Architect)

Copie o bloco abaixo para um arquivo novo:

```markdown
# ADR-XXX — Título

Status: Proposed | Accepted | Deprecated
Data: AAAA-MM-DD

## Contexto
Qual problema ou necessidade levou a esta decisão? Quais restrições existem?

## Decisão
O que foi decidido, de forma objetiva.

## Consequências
O que fica mais fácil, o que fica mais difícil e o que ainda está em aberto.
```
