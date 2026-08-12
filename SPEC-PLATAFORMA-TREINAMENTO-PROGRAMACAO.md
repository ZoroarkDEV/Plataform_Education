# SPECIFICAÇÃO COMPLETA — PLATAFORMA DE TREINAMENTO PARA INICIANTES EM PROGRAMAÇÃO

**Nome do Projeto (provisório):** PLATAFORM_EDUCATION 
**Versão do Documento:** 1.0.0  
**Data:** 2026-08-12  
**Autor:** Especificação para Agente de Desenvolvimento  
**Objetivo do Documento:** Permitir que um agente (Claude Code, Cline, Gemini Code Assist, etc.) desenhe, implemente, teste e valide **Front-end, Back-end, Banco de Dados, Features, Fluxos, Testes e Conteúdo Pedagógico** de forma autônoma e sem ambiguidade.

---

## 1. VISÃO GERAL DO PRODUTO

### 1.1 Problema
Iniciantes em programação se perdem facilmente:
- Não sabem por onde começar
- Escolhem linguagens aleatoriamente
- Não têm feedback real sobre o progresso
- Falta de estrutura progressiva e prática
- Ambiente local de estudo é complicado de montar (especialmente SQL + Docker)

### 1.2 Solução
Uma plataforma de aprendizado personalizado que:
1. Faz o usuário escolher um **segmento/objetivo de carreira** (ex: Backend .NET, Frontend Web, Data/AI, Fullstack, etc.)
2. Gera um **roadmap personalizado** com módulos, aulas, exercícios e projetos
3. Ensina **na prática** (código real, ambiente Docker, SQL, APIs)
4. Acompanha progresso, gera recomendações e valida habilidades
5. Começa como uso **pessoal** (single-user / local) e evolui para multi-usuário online

### 1.3 Público-Alvo
- Iniciantes absolutos e intermediários
- Pessoas que querem entrar no mercado de tecnologia
- Desenvolvedores que querem mudar de stack (ex: de JS para C#)
- Foco especial no usuário principal: **estudante de C# / .NET MVC**

### 1.4 Objetivos de Negócio / Uso
- **Fase 1 (Pessoal):** Rodar localmente, estudar C# + .NET + SQL com Docker, ter trilha completa.
- **Fase 2 (Online):** Multi-usuário, autenticação, progresso persistente, possível monetização futura.
- Ensinar o usuário da plataforma **como um developer real** (não só sintaxe): ambiente, Git, Docker, SQL, requisições HTTP, arquitetura básica.

---

## 2. STACK TECNOLÓGICA RECOMENDADA (ESCOLHA DEFINITIVA)

### 2.1 Backend (Prioridade máxima — alinhado ao seu estudo)
| Camada              | Tecnologia                          | Justificativa |
|---------------------|-------------------------------------|-------------|
| Linguagem           | **C# 13 / .NET 9 ou .NET 10**      | Seu objetivo principal de estudo |
| Framework Web       | **ASP.NET Core MVC** + Minimal APIs (híbrido) | Você quer estudar MVC. Usaremos Controllers + Views + algumas Minimal APIs |
| ORM                 | **Entity Framework Core 9+**        | Padrão de mercado .NET |
| Validação           | FluentValidation + Data Annotations | Boas práticas |
| Auth                | ASP.NET Identity + JWT (para API) + Cookie (para MVC) | Pronto para evoluir |
| Background Jobs     | Hangfire ou Quartz.NET              | Lembretes, geração de certificados |
| Documentação API    | Swagger / OpenAPI                   | Obrigatório |

### 2.2 Frontend
| Tecnologia                  | Uso |
|-----------------------------|-----|
| **Razor Views + Tag Helpers** | Páginas principais (MVC) — você estuda isso |
| **Blazor Server** (opcional fase 2) | Componentes interativos |
| **Alpine.js + Tailwind CSS** | Interatividade leve sem complexidade excessiva |
| **HTMX** (recomendado)      | Atualizações parciais sem SPA completa (excelente para aprendizado) |
| Chart.js / ApexCharts       | Progresso e dashboards |

### 2.3 Banco de Dados
- **PostgreSQL 16+** (principal) — via Docker
- **Redis** (cache + sessões + ranking)
- Entity Framework Core com Migrations

### 2.4 Infraestrutura / DevOps (essencial para ensinar o usuário)
- **Docker + Docker Compose** (obrigatório desde o dia 1)
- Git + GitHub
- .NET Aspire (opcional, moderno)
- Local: `docker-compose up`
- Produção futura: Azure App Service / Container Apps ou Railway / Render

### 2.5 Ferramentas de Desenvolvimento
- Visual Studio 2022/2026 ou VS Code + C# Dev Kit / SharpLsp
- Rider (opcional)
- pgAdmin / DBeaver
- Postman / Insomnia / Bruno

### 2.6 Linguagens e Conteúdos Prioritários (Mercado 2025-2026)
Ordem de prioridade baseada em demanda de vagas + utilidade:

1. **SQL** (quase universal — 45% das vagas)
2. **C# / .NET** (seu foco + enterprise)
3. **JavaScript + TypeScript**
4. **Python**
5. **HTML/CSS**
6. Git + Docker + Linux básico
7. Conceitos: HTTP, REST, OOP, Clean Code, Testes

---

## 3. ARQUITETURA DO SISTEMA

### 3.1 Visão de Alto Nível
```
[Browser] 
    ↓
[ASP.NET Core MVC + Razor + HTMX]
    ↓
[Controllers / Minimal APIs]
    ↓
[Application Services / Domain]
    ↓
[EF Core + PostgreSQL]  +  [Redis]
    ↓
[Docker Containers]
```

### 3.2 Estrutura de Pastas Recomendada (Clean-ish Architecture)
```
src/
├── DevPath.Web/                 # ASP.NET Core MVC Project
│   ├── Controllers/
│   ├── Views/
│   ├── ViewModels/
│   ├── wwwroot/
│   └── Program.cs
├── DevPath.Application/         # Casos de uso, DTOs, Interfaces
├── DevPath.Domain/              # Entidades, Enums, Value Objects
├── DevPath.Infrastructure/      # EF Core, Repositories, External services
├── DevPath.Content/             # Conteúdo pedagógico (JSON/Markdown/YAML)
└── tests/
    ├── DevPath.UnitTests/
    ├── DevPath.IntegrationTests/
    └── DevPath.E2ETests/
```

### 3.3 Princípios de Design
- **Separation of Concerns**
- **Dependency Injection** nativo do .NET
- **Repository + Unit of Work** (ou apenas DbContext com services)
- **CQRS leve** (Commands/Queries separados se necessário)
- Conteúdo pedagógico **desacoplado** (arquivos Markdown/JSON versionados)

---

## 4. MODELO DE DADOS (BANCO DE DADOS)

### 4.1 Entidades Principais

```csharp
// Domain Entities (resumo)

User
- Id (Guid)
- Email
- PasswordHash
- DisplayName
- AvatarUrl
- CreatedAt
- LastLoginAt
- PreferredLanguage (pt-BR, en, etc.)
- CurrentPathId (nullable)
- IsAdmin

CareerPath (Segmento)
- Id
- Title ("Backend .NET", "Frontend Web", "Data & AI", "Fullstack", "SQL & Dados")
- Description
- Icon
- DifficultyLevel
- EstimatedHours
- IsActive
- Order

Module
- Id
- CareerPathId
- Title
- Description
- Order
- EstimatedMinutes
- IsPublished

Lesson
- Id
- ModuleId
- Title
- ContentType (Markdown, Video, Interactive, Exercise, Project)
- Content (Markdown ou path para arquivo)
- Order
- EstimatedMinutes
- CodeLanguage (csharp, sql, javascript, python, etc.)
- Difficulty

Exercise
- Id
- LessonId
- Title
- Statement (Markdown)
- StarterCode
- SolutionCode (protegido)
- TestCases (JSON)
- Language
- MaxAttempts
- Points

UserProgress
- Id
- UserId
- LessonId / ExerciseId / ModuleId
- Status (NotStarted, InProgress, Completed, Mastered)
- Score
- TimeSpentSeconds
- CompletedAt
- LastAccessedAt

UserPathEnrollment
- UserId
- CareerPathId
- EnrolledAt
- ProgressPercentage
- CurrentModuleId
- IsActive

Achievement / Badge
- Id
- Title
- Description
- Icon
- Criteria (JSON)

UserAchievement
- UserId
- AchievementId
- EarnedAt

CodeSubmission
- Id
- UserId
- ExerciseId
- SubmittedCode
- Result (Pass/Fail)
- Feedback
- ExecutionTimeMs
- SubmittedAt
```

### 4.2 Relacionamentos Importantes
- User 1:N UserPathEnrollment
- CareerPath 1:N Module 1:N Lesson 1:N Exercise
- User 1:N UserProgress
- User 1:N CodeSubmission

### 4.3 Indexação
- Índices em: UserId + LessonId, CareerPathId + Order, etc.
- Full-text search em Lesson.Content (PostgreSQL tsvector)

---

## 5. FEATURES DETALHADAS (MVP → V1 → V2)

### 5.1 MVP (Uso Pessoal — Prioridade Máxima)

#### Autenticação & Onboarding
- [ ] Registro / Login (Identity)
- [ ] Onboarding: escolher **1 segmento** principal
- [ ] Geração automática do roadmap personalizado
- [ ] Dashboard inicial com progresso

#### Trilhas / Roadmaps
- [ ] Visualização de roadmap (árvore ou lista progressiva)
- [ ] Módulos e aulas bloqueados/desbloqueados por progresso
- [ ] Barra de progresso global e por módulo

#### Conteúdo de Aprendizado
- [ ] Aulas em Markdown renderizado (com syntax highlighting)
- [ ] Exercícios de código com editor embutido (Monaco Editor ou CodeMirror)
- [ ] Execução de código **segura** (para SQL e C# simples no início via Docker sandbox ou Roslyn)
- [ ] Feedback imediato nos exercícios

#### SQL + Docker (Feature Principal de Ensino)
- [ ] Guia completo: "Como montar ambiente local com Docker"
- [ ] Aulas interativas de SQL (SELECT, JOIN, INSERT, UPDATE, DELETE, indexes, transactions)
- [ ] Ambiente PostgreSQL rodando via Docker Compose
- [ ] Editor SQL no browser + execução real contra container
- [ ] Exercícios de modelagem de dados
- [ ] Requisições HTTP (HttpClient em C#) consumindo APIs de exemplo

#### Progresso & Gamificação Básica
- [ ] Marcação de aula como concluída
- [ ] Pontuação por exercício
- [ ] Badges simples (Primeiro SELECT, Primeiro Controller, etc.)

#### Administração de Conteúdo (você)
- [ ] CRUD de CareerPaths, Modules, Lessons, Exercises (área admin)

### 5.2 Versão 1.0 (Online Multi-usuário)
- [ ] Multi-tenancy leve / isolamento de progresso
- [ ] Perfil público de progresso
- [ ] Sistema de recomendações ("próximo passo sugerido")
- [ ] Certificados gerados (PDF)
- [ ] Fórum simples ou comentários por aula
- [ ] Modo offline / PWA (parcial)

### 5.3 Versão 2.0 (Avançada)
- [ ] AI Tutor (explicações personalizadas via LLM)
- [ ] Code Review automático
- [ ] Projetos finais com avaliação
- [ ] Integração com GitHub (enviar exercícios)
- [ ] Live coding sessions / pairing
- [ ] Marketplace de trilhas da comunidade

---

## 6. FLUXOS PRINCIPAIS (USER JOURNEYS)

### 6.1 Fluxo de Onboarding
1. Usuário acessa `/`
2. Clica em "Começar Agora"
3. Registra-se ou faz login
4. Escolhe segmento (ex: "Backend Developer com C# e .NET")
5. Sistema cria UserPathEnrollment + gera sequência de módulos
6. Redireciona para Dashboard `/dashboard`

### 6.2 Fluxo de Estudo de uma Aula
1. Usuário clica em um Module → Lesson
2. Sistema verifica se está desbloqueada
3. Renderiza conteúdo Markdown + vídeos (se houver)
4. Se tiver exercício → abre editor
5. Usuário envia código → backend executa testes → retorna resultado
6. Se passou → marca progresso + libera próxima aula
7. Atualiza percentual do path

### 6.3 Fluxo SQL + Docker (Destaque)
1. Aula: "Preparando o ambiente"
2. Usuário clica em "Iniciar Ambiente SQL"
3. Backend sobe (ou verifica) container PostgreSQL via Docker.DotNet ou script
4. Usuário recebe connection string temporária ou usa editor embutido
5. Executa queries reais
6. Sistema valida resultados contra test cases
7. Aula ensina: CREATE TABLE, INSERT, SELECT com JOINs, transactions, indexes, etc.

### 6.4 Fluxo de Projeto Final de Módulo
1. Usuário chega no projeto do módulo (ex: "API de Tarefas com ASP.NET Core MVC")
2. Recebe requisitos + starter template
3. Desenvolve localmente (ou no editor)
4. Faz upload do código ou conecta repositório
5. Sistema roda testes automatizados
6. Gera feedback + badge

---

## 7. CONTEÚDO PEDAGÓGICO — ESTRUTURA DAS TRILHAS

### 7.1 Trilha Principal: Backend .NET (seu foco)
**Módulo 0 — Fundamentos de Programação (comum a todas)**
- Lógica, variáveis, tipos, estruturas de controle, funções, OOP básico

**Módulo 1 — C# Fundamentos**
- Syntax, tipos, classes, interfaces, generics, LINQ, async/await

**Módulo 2 — SQL + Docker (obrigatório e detalhado)**
1. O que é banco relacional
2. Instalando Docker Desktop
3. docker-compose.yml com PostgreSQL
4. pgAdmin / DBeaver
5. CREATE DATABASE / TABLE / CONSTRAINTS
6. CRUD completo
7. JOINs (INNER, LEFT, RIGHT, FULL)
8. Agregações, GROUP BY, HAVING
9. Subqueries e CTEs
10. Indexes e performance básica
11. Transactions e ACID
12. Backup e restore
13. Conectando C# ao banco (ADO.NET + EF Core)

**Módulo 3 — ASP.NET Core MVC**
- Projeto do zero
- Controllers, Actions, Views, Razor
- Model Binding, Validation
- Tag Helpers
- Layouts e Partial Views
- Routing
- Dependency Injection
- Middleware
- Identity básico

**Módulo 4 — Entity Framework Core**
- DbContext, Migrations, Fluent API
- Relationships
- Queries eficientes
- Tracking vs NoTracking

**Módulo 5 — APIs e Requisições**
- Minimal APIs + Controllers
- HttpClient
- REST principles
- Status codes
- Swagger

**Módulo 6 — Projeto Final**
- Sistema completo (ex: Biblioteca, Task Manager, Blog simples) com MVC + EF + SQL + Docker

### 7.2 Outras Trilhas (para o mercado)
- **Frontend Web**: HTML → CSS → JS → TypeScript → React
- **Data & AI**: Python → SQL → Pandas → Visualização
- **Fullstack**: Combinação das anteriores
- **SQL Specialist**: Foco profundo em banco + performance + modelagem

### 7.3 Formato do Conteúdo
- Arquivos Markdown versionados em `/content`
- Frontmatter YAML com metadados
- Exemplos de código embutidos
- Exercícios com test cases em JSON

---

## 8. REQUISITOS NÃO-FUNCIONAIS

### Performance
- Páginas < 200ms (server-side)
- Editor de código responsivo
- Execução de exercícios com timeout (5-10s)

### Segurança
- Sanitização de código submetido (especialmente SQL e C#)
- Sandbox para execução de código (Docker containers efêmeros ou Roslyn scripting limitado)
- HTTPS, CORS configurado, rate limiting
- Proteção contra SQL Injection (sempre usar parâmetros)

### Usabilidade
- Interface limpa, dark mode
- Mobile-friendly (pelo menos leitura)
- Acessibilidade básica (WCAG 2.1 AA)

### Observabilidade
- Serilog + Seq ou Elasticsearch
- Application Insights (fase online)
- Health checks

---

## 9. TESTES (Obrigatórios)

### 9.1 Unit Tests
- Services de progresso
- Cálculo de percentual
- Validação de exercícios
- Domain logic

### 9.2 Integration Tests
- Controllers com TestServer
- EF Core com banco em memória ou Testcontainers
- Fluxo completo de conclusão de aula

### 9.3 E2E Tests
- Playwright ou Selenium
- Fluxo de onboarding completo
- Execução de exercício SQL
- Navegação no roadmap

### 9.4 Testes de Conteúdo
- Validar que todos os arquivos Markdown existem
- Validar test cases dos exercícios
- Validar ordem dos módulos

### 9.5 Cobertura Mínima
- Application layer: ≥ 80%
- Controllers críticos: ≥ 70%

---

## 10. PLANO DE IMPLEMENTAÇÃO RECOMENDADO (PARA O AGENTE)

### Fase 0 — Setup (1-2 dias)
1. Criar solution .NET
2. Configurar Docker Compose (PostgreSQL + Redis + app)
3. Configurar EF Core + Identity
4. Estrutura de pastas Clean Architecture
5. Tailwind + HTMX + Monaco Editor

### Fase 1 — Core Domain + Auth (2-3 dias)
- Entidades + Migrations
- Identity + Login/Registro
- Seed de CareerPaths iniciais

### Fase 2 — Roadmap & Progresso (3-4 dias)
- CRUD de Paths/Modules/Lessons (admin)
- Visualização de roadmap
- Sistema de progresso

### Fase 3 — Conteúdo + Editor (4-5 dias)
- Renderização Markdown
- Editor de código
- Sistema de submissão e validação de exercícios (começar com SQL)

### Fase 4 — SQL + Docker Experience (3 dias)
- Integração Docker
- Ambiente interativo de SQL
- Aulas completas de SQL

### Fase 5 — MVC Pedagógico + Projeto (contínuo)
- Conteúdo de C# e ASP.NET Core MVC
- Exercícios progressivos
- Projeto final

### Fase 6 — Polish + Testes + Documentação
- Testes automatizados
- README completo
- Scripts de seed de conteúdo

---

## 11. CRITÉRIOS DE ACEITE (Definition of Done)

Uma feature só é considerada pronta quando:
- [ ] Código implementado e revisado
- [ ] Testes unitários + integração passando
- [ ] Documentação atualizada (XML comments + README da feature)
- [ ] Conteúdo pedagógico correspondente criado
- [ ] Funciona no Docker Compose
- [ ] Progresso do usuário é persistido corretamente
- [ ] UI responsiva e acessível
- [ ] Logs adequados

---

## 12. MÉTRICAS DE SUCESSO (Uso Pessoal → Online)

**Pessoal:**
- Você consegue estudar C# + MVC + SQL do zero até um projeto completo usando a própria plataforma
- Tempo para montar ambiente SQL < 10 minutos
- Taxa de conclusão dos módulos > 70%

**Online (futuro):**
- Retention D7 > 40%
- % de usuários que completam o primeiro módulo
- NPS

---

## 13. RISCOS E MITIGAÇÕES

| Risco | Impacto | Mitigação |
|-------|---------|-----------|
| Execução de código malicioso | Alto | Sandbox Docker + limites de tempo/memória + whitelist de APIs |
| Conteúdo desatualizado | Médio | Conteúdo em Markdown versionado + easy update |
| Complexidade excessiva no início | Alto | Começar extremamente simples (MVP pessoal) |
| Performance do editor | Médio | Monaco Editor com lazy load |

---

## 14. PRÓXIMOS PASSOS IMEDIATOS PARA O AGENTE

1. Criar a solution com a estrutura de pastas definida
2. Configurar `docker-compose.yml` com PostgreSQL 16 + Redis + a aplicação
3. Implementar Identity + entidades básicas + migrations
4. Criar seed de pelo menos **2 CareerPaths**:
   - Backend .NET (completo)
   - SQL & Dados
5. Implementar o fluxo de onboarding + dashboard
6. Implementar a primeira aula interativa de SQL com Docker

---

## 15. APÊNDICE — EXEMPLO DE docker-compose.yml (inicial)

```yaml
version: '3.8'

services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: devpath
      POSTGRES_PASSWORD: devpath_secret
      POSTGRES_DB: devpath
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"

  # A aplicação será adicionada depois
  # app:
  #   build: .
  #   depends_on:
  #     - postgres
  #     - redis

volumes:
  postgres_data:
```

---

**Fim da Especificação v1.0**

Este documento é **auto-contido**. Qualquer agente de desenvolvimento deve conseguir:
- Entender o produto completo
- Escolher e justificar a stack
- Modelar o banco
- Implementar features prioritárias
- Criar testes
- Validar fluxos
- Evoluir o conteúdo pedagógico

Qualquer dúvida de interpretação deve ser resolvida priorizando:
1. Aprendizado real do usuário (especialmente C# + .NET MVC + SQL + Docker)
2. Simplicidade inicial (uso pessoal)
3. Escalabilidade futura (online)
```
Sobre o banco nosso Asuna já tem as credenciais de banco, permito criar o Banco de dados para nossa aplicação. se possível criar um container no docker e executar testes.