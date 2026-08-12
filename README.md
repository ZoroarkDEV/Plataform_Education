# DevPath — Plataforma de Treinamento para Iniciantes em Programação

> **Documento vivo para humanos e agentes de IA.**  
> Este README define a **filosofia, prioridades e regras de decisão** do projeto.  
> O agente orquestrador deve seguir este arquivo + o `SPEC-PLATAFORMA-TREINAMENTO-PROGRAMACAO.md` como fonte da verdade.

---

## 1. Propósito do Projeto (leia com atenção)

Este repositório contém a construção de uma **plataforma de ensino de programação** com dois objetivos simultâneos:

1. **Ensinar o usuário final** (iniciante) de forma estruturada e prática.
2. **Servir como projeto real de estudo** do dono do repositório em **C# + ASP.NET Core MVC + SQL + Docker**.

Ou seja: a própria plataforma é o laboratório de aprendizado.

### Filosofia central
- **Aprendizado real > features bonitas**
- **Simplicidade primeiro, complexidade depois**
- **Código que ensina** (limpo, legível, bem estruturado)
- **Uso pessoal primeiro**, multi-usuário depois
- **SQL + Docker são cidadãos de primeira classe** (não opcionais)

---

## 2. Documentos Oficiais (Ordem de Autoridade)

| Prioridade | Arquivo | Função |
|------------|---------|--------|
| 1 | `README.md` (este arquivo) | Filosofia, regras de decisão e viés do agente |
| 2 | `SPEC-PLATAFORMA-TREINAMENTO-PROGRAMACAO.md` | Especificação técnica completa |
| 3 | Código-fonte | Implementação real |

Quando houver conflito:
1. Siga a **filosofia deste README**
2. Depois a SPEC
3. Por último, “o que parece moderno/bonito”

---

## 3. Regras de Decisão para o Agente Orquestrador (VIÉS OBRIGATÓRIO)

### 3.1 Prioridades Absolutas (nesta ordem)

1. **Funcionar localmente com `docker-compose up`**
2. **Permitir estudar C# + ASP.NET Core MVC de forma real**
3. **Ensinar SQL do zero até manipulação + requisições**
4. **Progresso do usuário persistido e confiável**
5. **Código limpo e testável**
6. Beleza visual / animações / features avançadas

### 3.2 O que o agente DEVE fazer

- Preferir **ASP.NET Core MVC + Razor** (não fugir para Blazor ou SPA completa no início)
- Usar **Entity Framework Core** + Migrations
- Usar **PostgreSQL via Docker** (nunca SQLite como banco principal de produção/local definitivo)
- Manter conteúdo pedagógico em **Markdown/JSON** desacoplado do código
- Escrever testes (pelo menos unit + integration nos fluxos críticos)
- Documentar decisões importantes em comentários ou ADR simples
- Manter o projeto **rodável com poucos comandos**

### 3.3 O que o agente NÃO deve fazer (proibido no início)

- ❌ Transformar o projeto em SPA React/Vue/Angular no MVP
- ❌ Usar microserviços
- ❌ Adicionar Kafka, RabbitMQ, gRPC, etc. sem necessidade real
- ❌ Criar arquitetura excessivamente complexa (DDD completo, CQRS pesado, Event Sourcing)
- ❌ Priorizar UI perfeita antes do fluxo de aprendizado funcionar
- ❌ Executar código de usuário sem sandbox/limites
- ❌ Hardcodar conteúdo pedagógico dentro de Controllers ou Views

### 3.4 Viés pedagógico (muito importante)

O agente deve sempre perguntar mentalmente:

> “Isso ajuda o usuário (e o dono do projeto) a **aprender de verdade** como um developer trabalha?”

Exemplos de decisões corretas:
- Preferir mostrar `docker-compose.yml` real em vez de abstrair demais
- Preferir explicar Migrations em vez de esconder
- Preferir código legível em vez de “mágica”
- Preferir feedback claro nos exercícios em vez de apenas “passou/falhou”

---

## 4. Stack Oficial (não negociar no MVP)

| Camada | Tecnologia |
|--------|------------|
| Backend | **C# + ASP.NET Core MVC** (.NET 9 ou 10) |
| ORM | Entity Framework Core |
| Banco | PostgreSQL 16 (Docker) |
| Cache/Sessão | Redis (Docker) |
| Frontend | Razor Views + HTMX + Tailwind CSS |
| Editor de código | Monaco Editor ou CodeMirror |
| Auth | ASP.NET Identity |
| Testes | xUnit + FluentAssertions + Testcontainers (quando fizer sentido) |
| Container | Docker + Docker Compose |

> Qualquer mudança de stack precisa de justificativa explícita e aprovação implícita da filosofia deste README.

---

## 5. Como o Agente Deve Trabalhar (Processo)

### Ciclo recomendado

1. **Ler** este README + a SPEC
2. **Escolher a menor fatia vertical** que entrega valor (ex: login + dashboard vazio)
3. **Implementar**
4. **Testar** (manual + automatizado)
5. **Documentar** o que foi feito
6. **Só então** passar para a próxima fatia

### Ordem de implementação sugerida (não pular)

```
Fase 0 → Solution + Docker Compose + EF Core rodando
Fase 1 → Identity + entidades básicas + seed de CareerPaths
Fase 2 → Onboarding + Dashboard + progresso básico
Fase 3 → Renderização de aulas (Markdown)
Fase 4 → Exercícios SQL interativos + Docker
Fase 5 → Conteúdo C# + ASP.NET Core MVC
Fase 6 → Projeto final + testes + polish
```

### Definição de “Pronto”

Uma tarefa só está pronta quando:
- [ ] Funciona com `docker-compose up`
- [ ] Tem teste mínimo (quando aplicável)
- [ ] Não quebra o fluxo de aprendizado
- [ ] Está alinhada com a filosofia deste README
- [ ] Código está legível

---

## 6. Estrutura do Repositório (alvo)

```
/
├── README.md                          ← Você está aqui
├── SPEC-PLATAFORMA-TREINAMENTO-PROGRAMACAO.md
├── docker-compose.yml
├── src/
│   ├── DevPath.Web/                   # ASP.NET Core MVC
│   ├── DevPath.Application/
│   ├── DevPath.Domain/
│   └── DevPath.Infrastructure/
├── content/                           # Aulas e exercícios em Markdown/JSON
├── tests/
└── docs/                              # ADRs e decisões técnicas
```

---

## 7. Comandos Essenciais (alvo)

```bash
# Subir ambiente completo
docker-compose up -d

# Aplicar migrations
dotnet ef database update --project src/DevPath.Infrastructure

# Rodar a aplicação
dotnet run --project src/DevPath.Web

# Testes
dotnet test
```

---

## 8. Conteúdo Pedagógico — Regras

- Todo conteúdo de aula fica em `/content`
- Formato preferido: Markdown com frontmatter YAML
- Exercícios devem ter:
  - Enunciado claro
  - Starter code
  - Test cases
  - Solução (protegida)
- Nunca misturar conteúdo pedagógico com lógica de negócio

---

## 9. Segurança (não negociável)

- Nunca executar código de usuário sem isolamento
- Sempre usar parâmetros em SQL (nunca concatenação)
- Secrets apenas via variáveis de ambiente / user-secrets
- Rate limiting em endpoints de execução de código

---

## 10. Mensagem Final para o Agente

Você não está construindo “mais uma plataforma de cursos”.

Você está construindo:

> Uma ferramenta que ensina programação **do jeito que um developer real trabalha**,  
> enquanto o dono do projeto aprende **C# , ASP.NET Core MVC, SQL e Docker** construindo ela.

Cada decisão técnica deve servir a esse duplo propósito.

Quando em dúvida, escolha a opção que:
1. Ensina mais
2. É mais simples de rodar localmente
3. Mantém o foco em C# + MVC + SQL + Docker

---

**Última atualização:** 2026-08-12  
**Status:** Fase de especificação concluída → pronto para implementação
```
# Plataform_Education
