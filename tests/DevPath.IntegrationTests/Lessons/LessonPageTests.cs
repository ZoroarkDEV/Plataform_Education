using System.Net;
using DevPath.Application.Lessons;
using DevPath.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevPath.IntegrationTests.Lessons;

/// <summary>
/// Tela de aula de ponta a ponta: rota, [Authorize], controller e views. O banco de teste é vazio,
/// então o ILessonContentService real (AddInfrastructure) é TROCADO, só aqui, pelo
/// <see cref="InMemoryLessonContentService"/> (ConfigureTestServices + Replace), que imita as regras do real.
/// O exercício vem em LessonDto.Exercise, preenchido pelo dublê.
/// </summary>
[Collection(WebAppCollection.Name)]
public sealed class LessonPageTests(DevPathWebApplicationFactory factory)
{
    private const string Password = "Senha123";
    private const string Path = "sql-dados";
    private const string Module = "primeiros-passos";

    private static string Url(string lessonSlug) => $"/trilhas/{Path}/{Module}/{lessonSlug}";

    private static readonly InMemoryLessonContentService.FakeLesson Select = new(
        "seu-primeiro-select", "Seu primeiro SELECT", """
        # Seu primeiro SELECT

        ## O que você vai aprender

        - Item que vem do Markdown e não deve duplicar

        ## Lendo dados

        Use `SELECT`:

        ```sql
        SELECT titulo FROM livros;
        ```

        Isto não vira HTML: <script>alert('x')</script>

        ## Resumo

        SELECT lê dados.
        """, EstimatedMinutes: 15, Objectives: ["Ler dados com SELECT", "Escolher colunas"]);

    private static InMemoryLessonContentService CreateLessons(ExerciseDto? selectExercise = null) => new(
        new InMemoryLessonContentService.FakePath(Path, "SQL & Dados",
        [
            new(Module, "Primeiros passos",
            [
                new("introducao", "Introdução", "# Introdução\n\nOlá.", EstimatedMinutes: 5),
                new("aula-escondida", "Aula escondida", "# Escondida", IsPublished: false),
                Select with { Exercise = selectExercise },
                new("filtrando-com-where", "Filtrando com WHERE", "# rascunho", IsDraft: true),
                new("vazia", "Vazia", "   "),
            ]),
            new("modulo-fechado", "Módulo fechado", [new("aula", "Aula", "# A")], IsPublished: false),
        ]),
        new InMemoryLessonContentService.FakePath("trilha-inativa", "Inativa",
            [new("m", "M", [new("aula", "Aula", "# A")])], IsActive: false));

    private WebApplicationFactory<Program> App(ExerciseDto? exercise = null) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped<ILessonContentService>(_ => CreateLessons(exercise)))));

    // HTTPS: o redirecionamento do #9 (UseHttpsRedirection) responderia 307 a http://localhost.
    private static readonly Uri BaseAddress = new("https://localhost");

    private static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = BaseAddress });
        var response = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Aluna Aula",
            ["Email"] = $"aula-{Guid.NewGuid():N}@devpath.test",
            ["Password"] = Password,
            ["ConfirmPassword"] = Password,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect, "o cadastro deveria autenticar");
        return client;
    }

    [Fact]
    public async Task SemLogin_RedirecionaParaLoginComReturnUrl()
    {
        await using var app = App();
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = BaseAddress });
        var response = await client.GetAsync(Url("seu-primeiro-select"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should()
            .Contain("/Account/Login?ReturnUrl=" + Uri.EscapeDataString(Url("seu-primeiro-select")));
    }

    [Fact]
    public async Task AulaCompleta_RenderizaSecoesNaOrdemCodigoEBreadcrumb()
    {
        await using var app = App();
        var client = await LoggedInClientAsync(app);
        var response = await client.GetAsync(Url("seu-primeiro-select"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("<script>alert", "HTML cru do Markdown sai escapado");
        raw.Should().Contain("&lt;script&gt;alert");
        var html = WebUtility.HtmlDecode(raw);

        html.Should().Contain("<nav aria-label=\"Você está aqui\"");
        html.Should().Contain("aria-current=\"page\"").And.Contain("Aula 2 de 4", "a aula não publicada não conta");
        html.Should().Contain("~15 min").And.Contain("Iniciante");
        html.Should().Contain("<li>Ler dados com SELECT</li>");
        html.Should().NotContain("Item que vem do Markdown", "os objetivos do DTO substituem a seção do Markdown");
        html.Should().Contain("class=\"tok-keyword\">SELECT</span>");
        html.Should().Contain("data-copy-code").And.Contain("aria-live=\"polite\"");
        html.Should().NotContain("data-exercise", "sem exercício, a seção não aparece");

        var order = new[] { "id=\"titulo-aula\"", "Tempo estimado:", "Nível:", "O que você vai aprender", "data-lesson-content", "id=\"sec-resumo\"" }
            .Select(marker => html.IndexOf(marker, StringComparison.Ordinal)).ToList();
        order.Should().NotContain(-1);
        order.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task VizinhoRascunho_ApareceComoEmBreveSemLink()
    {
        await using var app = App();
        var client = await LoggedInClientAsync(app);
        var html = await (await client.GetAsync(Url("seu-primeiro-select"))).ReadHtmlAsync();

        html.Should().Contain($"href=\"{Url("introducao")}\" rel=\"prev\"", "a aula não publicada fica fora da navegação");
        html.Should().NotContain("Aula escondida");
        html.Should().Contain("Filtrando com WHERE").And.Contain("Em breve");
        html.Should().NotContain($"href=\"{Url("filtrando-com-where")}\"");
    }

    [Fact]
    public async Task ComExercicio_MostraSoAVisaoSeguraComDicasProgressivas()
    {
        await using var app = App(new ExerciseDto(
            "Catálogo de títulos",
            "Liste o **título** de cada livro.",
            "sql",
            "livraria",
            "SELECT \nFROM livros\n",
            ["Qual coluna guarda o título?", "Use SELECT titulo FROM livros;"]));
        var client = await LoggedInClientAsync(app);

        var html = await (await client.GetAsync(Url("seu-primeiro-select"))).ReadHtmlAsync();

        html.Should().Contain("Exercício: Catálogo de títulos");
        html.Should().Contain("<strong>título</strong>");
        html.Should().Contain("Dados usados:").And.Contain("livraria").And.Contain("10 pontos");
        html.Should().Contain("Código para começar").And.Contain("Copiar código SQL");
        html.Should().Contain("Ver dica 1 de 2").And.Contain("Ver dica 2 de 2");
        html.Should().Contain("data-hints");

        var order = new[] { "data-lesson-content", "data-exercise", "id=\"sec-resumo\"" }
            .Select(marker => html.IndexOf(marker, StringComparison.Ordinal)).ToList();
        order.Should().NotContain(-1);
        order.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ComExercicio_TitulosDoEnunciadoComecamEmH3SemSegundoH1()
    {
        await using var app = App(new ExerciseDto(
            "Catálogo de títulos",
            "# Objetivo\n\nListe os títulos.\n\n## Regras\n\nSó a coluna titulo.",
            "sql",
            "livraria",
            "SELECT \nFROM livros\n",
            []));
        var client = await LoggedInClientAsync(app);

        var html = await (await client.GetAsync(Url("seu-primeiro-select"))).ReadHtmlAsync();

        CountTag(html, "<h1").Should().Be(1, "o único <h1> é o título da aula");
        var exercise = html[html.IndexOf("data-exercise", StringComparison.Ordinal)..];
        exercise = exercise[..exercise.IndexOf("</section>", StringComparison.Ordinal)];
        CountTag(exercise, "<h2").Should().Be(1, "só o título da seção do exercício é <h2>");
        exercise.Should().Contain("<h3>Objetivo</h3>").And.Contain("<h4>Regras</h4>");
    }

    private static int CountTag(string html, string tag)
    {
        var count = 0;
        for (var i = html.IndexOf(tag, StringComparison.Ordinal); i >= 0; i = html.IndexOf(tag, i + tag.Length, StringComparison.Ordinal))
        {
            var next = i + tag.Length < html.Length ? html[i + tag.Length] : ' ';
            if (next is ' ' or '>') count++;
        }

        return count;
    }

    [Fact]
    public async Task AulaComDefeito_Devolve500ComMensagemAmigavelSemDetalheTecnico()
    {
        await using var app = App();
        var client = await LoggedInClientAsync(app);
        var response = await client.GetAsync(Url("vazia"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var html = await response.ReadHtmlAsync();
        html.Should().Contain("Não conseguimos carregar esta aula agora. Tente de novo ou volte ao dashboard.");
        html.Should().Contain("Voltar ao dashboard");
        html.Should().NotContain("Ver detalhe técnico", "fora de Development o detalhe técnico fica só no log");
        html.Should().NotContain("Exception");
    }

    [Fact]
    public async Task Rascunho_AcessoDireto_Devolve404EstaAulaEstaSendoEscrita()
    {
        await using var app = App();
        var client = await LoggedInClientAsync(app);
        var response = await client.GetAsync(Url("filtrando-com-where"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadHtmlAsync()).Should().Contain("Esta aula está sendo escrita");
    }

    [Fact]
    public async Task AulaInexistente_Devolve404Amigavel()
    {
        await using var app = App();
        var client = await LoggedInClientAsync(app);

        foreach (var url in new[]
                 {
                     Url("nao-existe"), Url("aula-escondida"), $"/trilhas/{Path}/modulo-fechado/aula",
                     "/trilhas/trilha-inativa/m/aula", $"/trilhas/trilha-que-nao-existe/{Module}/introducao",
                 })
        {
            var response = await client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
            (await response.ReadHtmlAsync()).Should().Contain("Não encontramos esta aula.");
        }
    }
}
