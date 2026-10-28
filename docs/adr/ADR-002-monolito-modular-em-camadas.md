# ADR-002 — Monólito modular em camadas

Status: Accepted
Data: 2026-10-28

## Contexto

O MVP é de **uso pessoal** e o projeto existe também para ensinar arquitetura de forma real, mas sem
excesso (README §1 e §3). A SPEC §3.2–3.3 propõe uma estrutura "Clean-ish" com Web, Application,
Domain e Infrastructure, Dependency Injection nativo e separação de responsabilidades. O README §3.3
proíbe no início: microserviços, Kafka/RabbitMQ/gRPC sem necessidade, DDD completo, CQRS pesado e
Event Sourcing. O DevPath Architect (`DEVPATH-AGENTS-SKILLS.md`) prefere monólito modular a
microserviços e clareza a pureza dogmática.

## Decisão

O DevPath é **um único monólito**, uma aplicação ASP.NET Core MVC, organizado em quatro camadas
dentro de `src/`:

| Projeto | Responsabilidade | Depende de |
|---------|------------------|------------|
| `DevPath.Domain` | Entidades, enums, value objects e regras de domínio | nada |
| `DevPath.Application` | Casos de uso / services, DTOs e interfaces | Domain |
| `DevPath.Infrastructure` | EF Core, acesso a dados, Redis e serviços externos | Application, Domain |
| `DevPath.Web` | Controllers, Views, ViewModels e `Program.cs` (composition root da DI) | Application, Infrastructure |

Regras:
- **Controllers finos:** recebem a requisição, chamam a camada Application e devolvem a View.
  Regra de negócio não fica em Controller nem em View.
- O Domain não conhece EF Core, ASP.NET nem infraestrutura.
- Dependency Injection nativo do .NET; nada de container de DI externo.
- Service layer simples; sem MediatR no início (`DEVPATH-AGENTS-SKILLS.md`, DevPath Backend .NET).
- Sem microserviços, sem message brokers e sem CQRS pesado ou Event Sourcing no MVP.
- Testes ficam em `tests/` na raiz (ex.: `tests/DevPath.UnitTests`), e não dentro de `src/` como no
  desenho da SPEC §3.2; prevalece a estrutura do README §6.

## Consequências

- **Mais fácil:** um só deploy e um só `docker-compose`; as camadas deixam o código testável (o
  Domain e a Application testam-se sem banco) e ensinam separação de responsabilidades na prática.
- **Mais difícil:** exige disciplina para não "pular" camadas (ex.: Controller usando `DbContext`
  direto); há um pouco mais de cerimônia que um projeto único.
- **Em aberto:**
  - Usar Repository + Unit of Work ou apenas `DbContext` com services (a SPEC §3.3 aceita os dois);
    decidir na implementação do EF Core (issue #8).
  - "CQRS leve" (separar Commands/Queries) só se surgir necessidade real (SPEC §3.3).
  - Minimal APIs ao lado dos Controllers (SPEC §2.1) ficam fora do MVP até haver um caso concreto.
