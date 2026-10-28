# Frontend: Razor + Tailwind CSS + HTMX

A UI do DevPath é renderizada no servidor com **Razor Views** (ASP.NET Core MVC).
Não há SPA nem Node/npm: o CSS vem do **Tailwind CSS (standalone CLI)** e a interatividade
parcial vem do **HTMX**.

## Onde fica cada coisa (`src/DevPath.Web`)

| Arquivo | Função |
|---------|--------|
| `Styles/app.css` | Entrada do Tailwind (tema, `@source`, estilos base). **Edite aqui.** |
| `wwwroot/css/site.css` | CSS gerado e minificado pelo Tailwind. **Não edite à mão.** É commitado. |
| `wwwroot/lib/htmx/htmx.min.js` | HTMX 2.0.10 versionado no repo (sem CDN). |
| `wwwroot/js/site.js` | JS mínimo do layout (menu mobile). |
| `Views/Shared/_Layout.cshtml` | Layout base: skip link, header/nav responsivo, `<main>`, footer. |
| `Views/Home/Index.cshtml` | Home: um único botão principal ("Começar Agora") e o "Como funciona". |

## Como o CSS é gerado

O `DevPath.Web.csproj` tem dois targets MSBuild:

1. **`TailwindDownload`**: na primeira build, baixa o CLI do Tailwind na versão fixada
   (`TailwindVersion`, hoje `v4.3.3`) para `src/DevPath.Web/.tools/tailwind/` (ignorado pelo git).
   Escolhe o binário certo para Linux, macOS ou Windows (x64/arm64).
2. **`TailwindBuild`**: antes de cada build, compila `Styles/app.css` para `wwwroot/css/site.css`
   (`--minify`). É incremental: só roda quando algum `.cshtml`, `.css` de `Styles/` ou `.js` mudou.

Ou seja, basta:

```bash
dotnet build      # ou dotnet run --project src/DevPath.Web
```

Sem internet, ou em builds de Docker/CI, o download falha com um aviso e a app usa o
`site.css` já commitado. Para pular o Tailwind de propósito:

```bash
dotnet build -p:TailwindBuild=false
```

> Ao mudar classes nas views, rode `dotnet build` e **commite o `wwwroot/css/site.css` atualizado**.

## Modo watch (desenvolvimento)

Em um terminal, deixe o Tailwind recompilando ao salvar:

```bash
cd src/DevPath.Web
./.tools/tailwind/v4.3.3/tailwindcss-linux-x64 -i Styles/app.css -o wwwroot/css/site.css --watch
# macOS: tailwindcss-macos-arm64 | Windows: .tools\tailwind\v4.3.3\tailwindcss-windows-x64.exe
```

Em outro terminal:

```bash
dotnet watch --project src/DevPath.Web
```

(Rode `dotnet build` uma vez antes para o CLI ser baixado.)

## HTMX

O HTMX 2.0.10 é carregado pelo `_Layout` em todas as páginas (`defer`), pronto para atualizações
parciais (`hx-get`/`hx-post` em uma action que devolve `PartialView(...)`, HTML sem layout).
A classe `.htmx-indicator` (em `Styles/app.css`) só aparece durante a requisição.
Para requisições que alteram dados (`hx-post`), envie o token antiforgery do ASP.NET Core.
O exemplo `GET /Home/ServerTime` do layout base (#5) foi removido da Home no #11 (decisão de produto).

## Por que não Bootstrap/jQuery?

O template `dotnet new mvc` traz Bootstrap, jQuery e jquery-validation. Eles foram removidos:
a stack oficial é Tailwind + HTMX. O `_ValidationScriptsPartial.cshtml` foi mantido como no-op
para não quebrar views que o referenciam; a validação fica no servidor (DataAnnotations + `ModelState`).

## Formulários e validação (telas de conta)

- `Views/Account/Login.cshtml`, `Register.cshtml` e `AccessDenied.cshtml` usam as classes de componente
  definidas em `Styles/app.css` (`form-label`, `form-input`, `btn-primary`, `field-validation-error`,
  `validation-summary-errors`...).
- Sem jQuery: o navegador valida com atributos HTML5 (`required`, `type="email"`, `minlength`/`maxlength`,
  `pattern`) que espelham as Data Annotations; o servidor sempre valida de novo (`ModelState`).
- `TagHelpers/AriaValidationTagHelper.cs` adiciona `aria-describedby="{id}-error"` em todo `input asp-for`
  e `aria-invalid="true"` quando o campo tem erro. O `<span asp-validation-for>` precisa de `id="{Campo}-error"`.
- `Views/Shared/_LoginPartial.cshtml` aparece no header (desktop) e no menu mobile
  (`view-data` com `Variant = "mobile"`). O logout é sempre um `<form method="post">` com antiforgery.

## Tela de aula (Markdown) — issue #12

Rota: `GET /trilhas/{pathSlug}/{moduleSlug}/{lessonSlug}` (`Controllers/LessonsController.cs`, fino, `[Authorize]`:
sem login vai para `/Account/Login?ReturnUrl=...`).

| Peça | Onde | O que faz |
|------|------|-----------|
| Contrato | `DevPath.Application/Lessons/` (do **Backend**) | `ILessonContentService.GetLessonAsync(...)` devolve `LessonLoadResult` (aula **ou** erro `NotFound`/`Malformed`/`Draft`, sem exceção). `Detail` é técnico (log / "Ver detalhe técnico"); o texto amigável a Web escolhe pelo `Kind`. |
| Serviço real | `DevPath.Infrastructure/Lessons/LessonContentService.cs` | Lê do banco (seed de `/content`), registrado em `AddInfrastructure`. Não há fake em Development; nos testes de integração a tela usa `InMemoryLessonContentService` (ConfigureTestServices + Replace), que imita as regras do real. |
| Markdown → HTML | `DevPath.Web/Lessons/LessonMarkdownRenderer.cs` | Markdig com `.DisableHtml()` (HTML cru sai escapado), links só `http(s)`/`mailto`/relativos (`javascript:` vira `#`), tabelas. Remove o `# Título` inicial e separa as seções `## O que você vai aprender` (usada só se o DTO não trouxer `LearningObjectives`) e `## Resumo`. |
| Exercício | `LessonDto.Exercise` (`ExerciseDto`, Application/Lessons), `Views/Lessons/_LessonExercise.cshtml` | Vem de dados estruturados (`NN-aula.exercise.json`), **não** do Markdown. A tela só recebe a visão segura `ExerciseDto` do Backend (título, enunciado em Markdown, linguagem, dataset, pontos, código inicial só leitura, dicas): **nunca** solução, linhas esperadas dos `test_cases` nem `common_errors`. Dicas em `<details>`, liberadas uma a uma com JS. `Exercise = null` (aula sem exercício) esconde a seção; o código inicial só aparece quando `StarterCode` não é `null`. |
| Destaque de código | `DevPath.Web/Lessons/CodeHighlighter.cs` | No servidor, com ColorCode, só **SQL e C#** (sem JS, sem CDN). Gera `<span class="tok-...">`; cores em `Styles/app.css`. Outras linguagens saem como texto escapado. |
| Views | `Views/Lessons/Show.cshtml`, `_LessonPager.cshtml`, `Unavailable.cshtml` | Ordem: breadcrumb, anterior/próxima, título, tempo (`~15 min`), nível (pt-BR), objetivos, conteúdo, exercício, resumo, anterior/próxima. |
| JS | `wwwroot/js/site.js` | Botão **Copiar** (aparece só com JS; anuncia em `#copy-status`, `aria-live`) e botão de tema. |

Regras de tela:

- **Rascunho** (`LessonLink.IsDraft`): "Em breve", sem link; acesso direto = HTTP 404 com "Esta aula está sendo escrita".
- **Aula com defeito / erro inesperado**: "Não conseguimos carregar esta aula agora. Tente de novo ou volte ao dashboard."
  (HTTP 500) e `LogError` com o detalhe. "Ver detalhe técnico" (recolhido) só aparece em Development.
- **Não encontrada / slug inválido**: página amigável com HTTP 404.
- **Tema escuro**: variante `dark:` por classe (`.dark` no `<html>`), ligada pela preferência do sistema ou pelo botão
  (salvo em `localStorage`). Desde o #11 vale para o app inteiro (veja "Modo escuro em todo o app" abaixo); a aula e a
  página de aula indisponível também têm classes `dark:`.
- Blocos de código: fundo escuro nos dois temas, `overflow-x: auto`, `tabindex="0"` (rolagem pelo teclado), fonte 16 px.

Testar no navegador (Development, logado): as aulas do seed, ex. `/trilhas/sql-dados/ambiente/o-que-e-banco-relacional`
(veja os slugs na tabela `lessons`); `.../nao-existe` (404).

Contrastes medidos no Chrome (WCAG AA pede 4.5:1 para texto normal): texto da aula 9.9:1 (claro) / 13.6:1 (escuro);
links 7.5:1 / 16.3:1; código base 14.5:1; tokens sobre `#0f172b`: keyword 9.9, string 12.7, number 12.4,
comment 7.0, function 12.3, type 9.7, operator 9.8; rodapé 4.8:1 / 6.8:1.

## Onboarding e dashboard — issue #11

**Rotas (todas exigem login):**

| Rota | O que faz |
|---|---|
| `GET /escolha-sua-trilha` | Lista as trilhas ativas. Quem já tem trilha vai direto para `/Dashboard`. |
| `POST /escolha-sua-trilha` (antiforgery) | Matricula na trilha escolhida (`pathSlug`) e redireciona para `/Dashboard` com um aviso que aparece uma vez (TempData). |
| `GET /Dashboard` | Trilha atual, progresso, botão "Continuar" e o roadmap. Quem não tem trilha volta para `/escolha-sua-trilha`. |

Depois do cadastro o usuário vai para `/escolha-sua-trilha` (antes ia para `/Dashboard`).

**Porta da Web e adapter:** os controllers dependem de `Onboarding/IStudentJourneyGateway`, que
devolve view models prontos. A implementação registrada em `Program.cs` é `Onboarding/StudentJourneyGateway`,
um adapter fino para o `IStudentJourneyService` do backend (`DevPath.Application.Journey`, registrado em
`AddInfrastructure`). Ele só mapeia, campo a campo:

| DTO do backend | View model da Web |
|---|---|
| `PathSummaryDto` | `PathOptionViewModel` |
| `LessonRefDto` | `LessonRef` |
| `DashboardLessonDto` (com `UnlockRequirement`) | `LessonItemViewModel` |
| `DashboardModuleDto` | `ModuleProgressViewModel` |
| `DashboardDto` | `DashboardViewModel` |
| `JourneyLessonStatus` | `LessonStatus` (`switch` explícito, nunca cast) |
| `EnrollmentResult.Kind` | `EnrollmentOutcome.Status` (`switch` explícito) |

Casos de borda:
- `EnrollmentResultKind.NoLessons` (trilha ativa sem nenhuma aula disponível, inclusive só com rascunhos) vira
  `EnrollmentStatus.NoLessons`: volta para a escolha com "Essa trilha ainda não tem aulas. Escolha outra trilha ou
  volte em breve.".
- `AlreadyEnrolled` (2ª matrícula, na mesma trilha ou em outra; não há troca de trilha) vai para o
  dashboard com o aviso "Você já está na trilha X. Por enquanto é uma trilha por vez: continue de onde parou.",
  com X = `EnrollmentResult.PathTitle` (a trilha atual). Sem título, o aviso fica genérico ("Você já está em uma trilha.").
- `EnrollmentResultKind.UserNotFound` (conta apagada com o cookie ainda válido) vira `EnrollmentStatus.UserNotFound`:
  o controller encerra a sessão e manda para `/Account/Login`, sem 500. O serviço não lança para os casos
  esperados, então o adapter não captura exceção nenhuma. `GetDashboardAsync` devolve `null` nesse caso (o mesmo
  de "sem trilha"), então o dashboard manda para a escolha, sem 500.
- O título vem de `EnrollmentResult.PathTitle`, lido só quando `HasPath` é true (Enrolled e AlreadyEnrolled).
- Porcentagens e "X de Y aulas" usam `AvailableLessons` (sem rascunho). O `TotalLessons` do backend (com
  rascunhos) não é usado no dashboard: o "Aula X de Y" é da página de aula (#12).

Os dados fictícios ficam só nos testes (`tests/DevPath.IntegrationTests/Onboarding/FakeStudentJourneyGateway.cs`,
trocado por DI nos testes de tela). `OnboardingRealServiceTests` roda o fluxo com o serviço real e um Postgres
do Testcontainers.

Contrato que a Web usa do backend:
- trilhas ativas com o número de aulas disponíveis (sem rascunho);
- se o usuário já tem trilha atual;
- matrícula com resultado `Enrolled | AlreadyEnrolled | PathNotFound | NoLessons | UserNotFound`, com
  `PathTitle`/`HasPath` (título da trilha escolhida ou da atual);
- dashboard com status de cada aula já calculado (`Completed | InProgress | Available | Locked | ComingSoon`),
  porcentagens, próxima aula, `IsFirstAccess` e a aula que libera cada aula bloqueada;
- módulos na ordem da trilha e aulas na ordem do módulo. A tela numera "Módulo 1, 2..." pela posição.
  Na trilha SQL & Dados, o módulo 1 é "Primeiros passos com SQL" (decisão do #22), com JOIN antes de GROUP BY.

**Estados do dashboard:** `Empty` (trilha sem aulas publicadas: sem barra e sem "0 de 0"),
`Continue` (um único `btn-primary`: "Começar pela aula 1" no primeiro acesso, "Continuar" depois) e
`AllDone` ("Você concluiu tudo que está disponível 🎉"). Cada status tem texto além da cor, e as aulas
bloqueadas ou "Em breve" não têm link. O motivo do bloqueio ("Conclua 'X' para liberar esta aula") fica
**sempre visível** abaixo do título, sem depender de hover ou foco (funciona no celular e no teclado).

**Um botão principal por tela:** cada tela tem no máximo um `btn-primary` (Home, Entrar, Criar conta,
Acesso negado e Dashboard têm exatamente um). O "Criar conta" do cabeçalho é contornado (secundário), e
os cards de "Escolha sua trilha" usam `btn-secondary`, porque as trilhas são opções equivalentes.

**Modo escuro em todo o app:** o `_Layout` aplica o tema em todas as páginas, a menos que a view
defina `ViewData["SupportsDarkMode"] = false`. A preferência do sistema vale até o usuário clicar
no botão de tema do cabeçalho, e a escolha fica salva em `localStorage["devpath-theme"]`. O
mecanismo é o mesmo da página de aula (#12). O `site.js` mantém `aria-pressed` igual em todos os
botões de tema da página.

**Contraste medido (WCAG AA, Chrome headless, cores calculadas na página):**

| Elemento | Claro | Escuro | Mínimo |
|---|---|---|---|
| Menor texto do app (rodapé) | 4.76 | 6.78 | 4.5 |
| `btn-primary` (branco em brand-600) | 6.08 | 6.08 | 4.5 |
| Links | 7.47 | 9.10 | 4.5 |
| `form-hint` | 7.58 | 6.78 | 4.5 |
| Erro de validação | 6.42 | 9.29 | 4.5 |
| Badge Concluída | 6.81 | 12.37 | 4.5 |
| Badge Em andamento | 8.73 | 12.05 | 4.5 |
| Badge Liberada | 7.13 | 9.78 | 4.5 |
| Badge Bloqueada | 9.45 | 9.83 | 4.5 |
| Badge Em breve | 10.36 | 12.00 | 4.5 |
| Texto de bloqueio ("Conclua …") | 7.58 | 12.00 | 4.5 |
| Barra de progresso (preenchimento × trilho) | 4.93 | 5.29 | 3 |
| Borda do campo de formulário | 4.76 | 3.74 | 3 |
| Contorno de foco | 6.08 | 9.10 | 3 |
