# ADR-001 — Stack oficial do MVP

Status: Accepted
Data: 2026-10-28

## Contexto

O DevPath tem um propósito duplo (README §1): ensinar programação a iniciantes e ser o laboratório
de estudo do dono do projeto em **C# + ASP.NET Core MVC + SQL + Docker**. A stack, portanto,
precisa ser justamente a que ele quer aprender, além de ser simples de rodar localmente
(prioridade nº 1 do README §3.1: funcionar com `docker-compose up`).

O README §4 define a "Stack Oficial (não negociar no MVP)". A SPEC §2 traz uma lista mais ampla,
com itens que não aparecem no README (Minimal APIs híbridas, JWT para API, FluentValidation,
Hangfire/Quartz.NET, Swagger, Alpine.js, Blazor Server opcional, .NET Aspire). Pela ordem de
autoridade (README > SPEC), o núcleo do MVP é o que está no README.

## Decisão

Para o MVP, a stack oficial é:

| Camada | Tecnologia |
|--------|------------|
| Linguagem / runtime | **C# / .NET 9** (a solution da Fase 0 usa `net9.0`; o README aceita .NET 9 ou 10) |
| Web | **ASP.NET Core MVC** com **Razor Views** |
| Interatividade no front | **HTMX** (atualizações parciais, sem SPA) |
| Estilo | **Tailwind CSS** |
| ORM | **Entity Framework Core** com Migrations |
| Banco | **PostgreSQL 16** via Docker |
| Cache / sessão | **Redis 7** via Docker |
| Autenticação | **ASP.NET Identity** |
| Testes | **xUnit + FluentAssertions** (Testcontainers quando fizer sentido) |
| Ambiente | **Docker + Docker Compose** |

Regras derivadas do README §3.2–3.3:
- Nada de SPA (React/Vue/Angular) nem Blazor no MVP.
- PostgreSQL é o banco principal local e definitivo; SQLite não é usado como banco principal.
- FluentAssertions fica fixado na linha **7.x**, porque a v8 passou a ter licença comercial (decisão
  registrada no PR #3).

## Consequências

- **Mais fácil:** tudo o que é construído serve diretamente ao estudo de C#, MVC, EF Core, SQL e
  Docker; o ambiente sobe com poucos comandos; o time de agentes tem uma lista fechada de tecnologias.
- **Mais difícil:** interatividade rica fica limitada ao que Razor + HTMX conseguem oferecer; trocar
  qualquer item da tabela exige um novo ADR com justificativa (README §4).
- **Em aberto (cada um vira ADR próprio quando for decidido):**
  - Editor de código: Monaco Editor ou CodeMirror (README §4 deixa as duas opções).
  - Itens só da SPEC, fora do núcleo do MVP: Minimal APIs, JWT, FluentValidation, Hangfire/Quartz.NET,
    Swagger, Alpine.js, Blazor Server, .NET Aspire.
  - Migração para .NET 10, se fizer sentido no futuro.
