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
| `Views/Home/Index.cshtml` + `_ServerTime.cshtml` | Home + exemplo de HTMX (`hx-get` em `HomeController.ServerTime`). |

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

## HTMX: o exemplo da Home

```html
<button hx-get="/Home/ServerTime" hx-target="#server-time" hx-swap="innerHTML">…</button>
<div id="server-time">…</div>
```

O clique faz um `GET /Home/ServerTime`; a action devolve `PartialView("_ServerTime", …)` (HTML sem layout)
e o HTMX troca só o conteúdo de `#server-time`. Para requisições que alteram dados (`hx-post`),
será preciso enviar o token antiforgery do ASP.NET Core.

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
