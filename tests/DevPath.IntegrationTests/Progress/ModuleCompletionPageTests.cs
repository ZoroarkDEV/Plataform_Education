using System.Net;
using DevPath.Application.Lessons;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.IntegrationTests.Lessons;
using DevPath.Web.Models.Lessons;
using DevPath.Web.Onboarding;
using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Samples = DevPath.IntegrationTests.Progress.FakeLessonProgressGateway.Samples;

namespace DevPath.IntegrationTests.Progress;

/// <summary>
/// Tela "Módulo concluído" (#42, US-M.1) com dublês: progresso da aula, dashboard e textos do conteúdo.
/// O fluxo com o serviço real (banco) está em <see cref="ModuleCompletionRealServiceTests"/>.
/// </summary>
[Collection(WebAppCollection.Name)]
public sealed class ModuleCompletionPageTests(DevPathWebApplicationFactory factory)
{
    private const string Path = "sql-dados";
    private const string Module = "primeiros-passos";
    private const string Lesson = "seu-primeiro-select";
    private const string Learned = "Pedir dados a uma tabela com SELECT e FROM e ler o resultado como uma planilha.";
    private static string LessonUrl => $"/trilhas/{Path}/{Module}/{Lesson}";
    private static string CompleteUrl => $"{LessonUrl}/concluir";
    private static string SummaryUrl => $"/trilhas/{Path}/modulos/{Module}/concluido";

    private static readonly LessonRef Like = new(Path, "mais-consultas-com-sql", "buscando-com-like", "Buscando com LIKE");

    private static ModuleProgressViewModel Module1(bool done) => new(Module, "Primeiros passos", done ? 1 : 0, 1, done ? 100 : 0,
        [new(Samples.Select, done ? LessonStatus.Completed : LessonStatus.Available, Points: done ? 10 : null)]);

    private static ModuleProgressViewModel Drafts() => new("ambiente", "Ambiente", 0, 0, 0,
        [new(new LessonRef(Path, "ambiente", "o-que-e", "O que é"), LessonStatus.ComingSoon)]);

    private static ModuleProgressViewModel Module2() => new("mais-consultas-com-sql", "Mais consultas com SQL", 0, 1, 0,
        [new(Like, LessonStatus.Available)]);

    private static DashboardViewModel WithModule2() =>
        new(Path, "SQL & Dados", 1, 2, 50, Like, false, [Module1(true), Drafts(), Module2()], 10);

    private static DashboardViewModel OnlyDraftsAfter() =>
        new(Path, "SQL & Dados", 1, 1, 100, null, false, [Module1(true), Drafts()], 10);

    private static DashboardViewModel NotDone() =>
        new(Path, "SQL & Dados", 0, 1, 0, Samples.Select, true, [Module1(false), Drafts()]);

    private sealed class FixedJourney(DashboardViewModel dashboard) : IStudentJourneyGateway
    {
        public Task<IReadOnlyList<PathOptionViewModel>> GetActivePathsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PathOptionViewModel>>([]);
        public Task<bool> HasCurrentPathAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<EnrollmentOutcome> EnrollAsync(Guid userId, string pathSlug, CancellationToken cancellationToken) =>
            Task.FromResult(EnrollmentOutcome.AlreadyEnrolled(dashboard.PathTitle));
        public Task<DashboardViewModel?> GetDashboardAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<DashboardViewModel?>(dashboard);
    }

    private sealed class FixedContent : IModuleCompletionContentSource
    {
        public Task<ModuleCompletionContent> GetAsync(string pathSlug, string moduleSlug, CancellationToken cancellationToken) =>
            Task.FromResult(new ModuleCompletionContent(
                "Você fez um banco de dados de verdade responder às suas perguntas, direto no navegador.",
                new Dictionary<string, string> { [Lesson] = Learned }));
    }

    private sealed class EmptyContent : IModuleCompletionContentSource
    {
        public Task<ModuleCompletionContent> GetAsync(string pathSlug, string moduleSlug, CancellationToken cancellationToken) =>
            Task.FromResult(ModuleCompletionContent.Empty);
    }

    private static InMemoryLessonContentService Lessons() => new(
        new InMemoryLessonContentService.FakePath(Path, "SQL & Dados",
        [
            new(Module, "Primeiros passos",
            [
                new(Lesson, "Seu primeiro SELECT", "# Seu primeiro SELECT\n## O que você vai aprender\n- SELECT\n## Resumo\nFeito.",
                    EstimatedMinutes: 15, Objectives: ["Ler dados"]),
            ]),
        ]));

    private WebApplicationFactory<Program> App(DashboardViewModel dashboard, LessonProgressView before, LessonProgressView after,
        bool withContent = true) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            var fake = new FakeLessonProgressGateway
            {
                Open = (_, _, _, _) => Samples.Allowed(before),
                GetState = (_, _, _, _) => Samples.Allowed(after),
                Complete = (_, _, _, _) => new LessonCompletionOutcome(LessonCompletionStatus.Completed, after),
            };
            services.Replace(ServiceDescriptor.Scoped<ILessonContentService>(_ => Lessons()));
            services.Replace(ServiceDescriptor.Scoped<ILessonProgressGateway>(_ => fake));
            services.Replace(ServiceDescriptor.Scoped<IStudentJourneyGateway>(_ => new FixedJourney(dashboard)));
            services.Replace(ServiceDescriptor.Singleton<IModuleCompletionContentSource>(
                withContent ? new FixedContent() : new EmptyContent()));
        }));

    private static LessonProgressView ReadingBefore() => Samples.ReadingAvailable(new LessonItemViewModel(Like, LessonStatus.Locked, Samples.Select));
    private static LessonProgressView ReadingDone() => Samples.ReadingCompleted() with { Next = new LessonItemViewModel(Like, LessonStatus.Available) };

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost")
        });
        var response = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Aluna Módulo",
            ["Email"] = $"mod-{Guid.NewGuid():N}@devpath.test",
            ["Password"] = "Senha123",
            ["ConfirmPassword"] = "Senha123",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private static async Task<string> HtmxCompleteAsync(HttpClient client)
    {
        var page = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        var token = System.Text.RegularExpressions.Regex.Match(
            page, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""").Groups[1].Value;
        using var req = new HttpRequestMessage(HttpMethod.Post, CompleteUrl);
        req.Headers.Add("HX-Request", "true");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        var response = await client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.ReadHtmlAsync();
    }

    [Fact]
    public async Task Htmx_UltimaAula_MostraModuloConcluido_NoLugarDaProxima_ComBotaoParaOModulo2()
    {
        await using var app = App(WithModule2(), ReadingBefore(), ReadingDone());
        var client = await LoggedInAsync(app);

        var body = await HtmxCompleteAsync(client);

        body.Should().Contain("data-module-completed=\"primeiros-passos\"");
        body.Should().MatchRegex(@"<h2 id=""modulo-concluido-titulo""[^>]*>\s*<span data-completion-message tabindex=""-1"">Módulo concluído: Primeiros passos</span>\s*<span aria-hidden=""true"">🎉</span>");
        body.Should().Contain("Você fez um banco de dados de verdade responder às suas perguntas, direto no navegador.");
        body.Should().MatchRegex(@"<h3[^>]*>O que você aprendeu</h3>");
        body.Should().Contain($"<li class=\"break-words\">{Learned}</li>");
        body.Should().Contain("+10 pontos neste módulo · Trilha SQL & Dados: 50%");
        body.Should().MatchRegex($@"<a href=""{Like.Url}""[^>]*data-next-module>Começar o módulo 3: Mais consultas com SQL</a>");
        body.Should().MatchRegex(@"class=""btn-primary[^""]*""[^>]*data-next-module", "btn-primary tem min-h-11 (44px)");
        body.Should().Contain("data-just-completed=\"true\"");
        body.Should().NotContain("id=\"next-lesson-link\"", "o bloco fica no lugar da \"Próxima aula →\"");
        body.Should().NotContain(LessonCompletionTexts.NextLabel);
        body.Should().NotContain(LessonCompletionTexts.SuccessModule("Primeiros passos"));
        body.Should().NotContain(ModuleCompletionTexts.EverythingDone);
    }

    [Fact]
    public async Task Htmx_SemModuloSeguintePronto_TextoDeTudoPronto_SemBotaoParaRascunho()
    {
        await using var app = App(OnlyDraftsAfter(), Samples.ReadingAvailable(Samples.ComingSoon(Samples.Where)),
            Samples.ReadingCompleted() with { Next = Samples.ComingSoon(Samples.Where), PathProgressPercentage = 100 });
        var client = await LoggedInAsync(app);

        var body = await HtmxCompleteAsync(client);

        body.Should().Contain(
            "Você terminou tudo o que está pronto até agora. Novas aulas estão chegando, e seu progresso fica guardado.");
        body.Should().NotContain("data-next-module");
        body.Should().NotContain("Começar o módulo");
        body.Should().Contain("data-back-to-path").And.Contain(LessonCompletionTexts.BackToPath);
        body.Should().Contain("+10 pontos neste módulo · Trilha SQL & Dados: 100%");
    }

    [Fact]
    public async Task SemTextosDoConteudo_EscondeOQueVoceAprendeu_ENaoQuebra()
    {
        await using var app = App(WithModule2(), ReadingBefore(), ReadingDone(), withContent: false);
        var client = await LoggedInAsync(app);

        var body = await HtmxCompleteAsync(client);

        body.Should().Contain("Módulo concluído: Primeiros passos");
        body.Should().NotContain("O que você aprendeu");
        body.Should().NotContain("data-module-subtitle");
        body.Should().Contain("data-next-module");
    }

    [Fact]
    public async Task ConclusaoDepoisDoPass_MostraOBloco_MasNaoQuandoJaEstavaConcluida()
    {
        var done = Samples.ExercisePassedCompleted() with { Next = new LessonItemViewModel(Like, LessonStatus.Available) };
        await using var app = App(WithModule2(), Samples.ExerciseNotPassed(), done);
        var client = await LoggedInAsync(app);

        var first = await (await client.GetAsync($"{LessonUrl}/conclusao")).ReadHtmlAsync();
        first.Should().Contain("data-module-completed").And.Contain("data-announce=\"true\"");
        first.Should().NotContain("data-just-completed", "depois do pass só anuncia, o foco fica no resultado");

        var again = await (await client.GetAsync($"{LessonUrl}/conclusao?antes=SkippedAfterFailures")).ReadHtmlAsync();
        again.Should().NotContain("data-module-completed", "o módulo já tinha sido concluído pelo Seguir sem pontos");
    }

    [Fact]
    public async Task SemHtmx_PrgVaiParaATela_ComH1_EVoltarParaATrilha()
    {
        await using var app = App(WithModule2(), ReadingBefore(), ReadingDone());
        var client = await LoggedInAsync(app);

        var post = await client.SubmitFormAsync(LessonUrl, CompleteUrl, new Dictionary<string, string>());
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Be(SummaryUrl);

        var page = await client.GetAsync(SummaryUrl);
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await page.ReadHtmlAsync();
        html.Should().MatchRegex(@"<h1 id=""modulo-concluido-titulo""[^>]*>\s*<span\s*>Módulo concluído: Primeiros passos</span>");
        html.Should().MatchRegex(@"<h2[^>]*>O que você aprendeu</h2>");
        html.Should().Contain("data-next-module").And.Contain("data-back-to-path");
        html.Should().Contain("<title>Módulo concluído: Primeiros passos");
    }

    [Fact]
    public async Task Tela_ModuloNaoConcluido_VoltaAoDashboard_EOutroModulo404()
    {
        await using var app = App(NotDone(), Samples.ReadingAvailable(), Samples.ReadingAvailable());
        var client = await LoggedInAsync(app);

        var notDone = await client.GetAsync(SummaryUrl);
        notDone.StatusCode.Should().Be(HttpStatusCode.Redirect);
        notDone.Headers.Location!.ToString().Should().StartWith("/Dashboard");

        (await client.GetAsync($"/trilhas/{Path}/modulos/nao-existe/concluido")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/trilhas/{Path}/modulos/ambiente/concluido")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "módulo só com rascunhos não tem resumo");
    }

    [Fact]
    public async Task Dashboard_ModuloConcluidoComLinkParaOResumo_EProximoPassoNoModuloSeguinte()
    {
        await using var app = App(WithModule2(), ReadingBefore(), ReadingDone());
        var client = await LoggedInAsync(app);

        var html = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();

        html.Should().MatchRegex($@"<a href=""{SummaryUrl}""[^>]*min-h-11[^>]*data-module-completed-link>\s*<span aria-hidden=""true"">Concluído ✓</span>\s*<span class=""sr-only"">Módulo concluído. Ver o resumo do módulo Primeiros passos</span>");
        html.Should().Contain("data-next-module-step").And.Contain("Próximo passo: módulo 3, Mais consultas com SQL");
        html.Should().Contain($"href=\"{Like.Url}\" class=\"btn-primary");
        html.Split("data-module-completed-link").Length.Should().Be(2, "só o módulo concluído tem o selo");
    }
}
