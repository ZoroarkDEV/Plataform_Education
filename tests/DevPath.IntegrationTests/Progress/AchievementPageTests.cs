using System.Net;
using System.Text.RegularExpressions;
using DevPath.Application.Lessons;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.IntegrationTests.Lessons;
using DevPath.IntegrationTests.Onboarding;
using DevPath.Web.Models.Lessons;
using DevPath.Web.Onboarding;
using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevPath.IntegrationTests.Progress;

/// <summary>Tela do selo "Primeiro SELECT" (#45, US-BADGE.1): dashboard e aviso na conclusão.</summary>
[Collection(WebAppCollection.Name)]
public sealed class AchievementPageTests(DevPathWebApplicationFactory factory)
{
    private const string Password = "Senha123";
    private const string Path = "sql-dados";
    private const string Module = "primeiros-passos";
    private const string Lesson = "seu-primeiro-select";
    private static string LessonUrl => $"/trilhas/{Path}/{Module}/{Lesson}";
    private static string CompleteUrl => $"{LessonUrl}/concluir";
    private static string ConclusaoUrl => $"{LessonUrl}/conclusao";
    private static readonly Uri BaseAddress = new("https://localhost");

    private static readonly DateTimeOffset EarnedAt =
        new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);

    private static EarnedAchievement FirstSelect => new(
        AchievementTexts.FirstSelectTitle,
        AchievementTexts.FirstSelectDescription,
        EarnedAt);

    private static readonly InMemoryLessonContentService.FakeLesson Select = new(
        Lesson, "Seu primeiro SELECT", """
        # Seu primeiro SELECT
        ## O que você vai aprender
        - SELECT
        ## Conteúdo
        ```sql
        SELECT 1;
        ```
        ## Resumo
        Feito.
        """, EstimatedMinutes: 15, Objectives: ["Ler dados"]);

    private static InMemoryLessonContentService Lessons(ExerciseDto? exercise = null) => new(
        new InMemoryLessonContentService.FakePath(Path, "SQL & Dados",
        [
            new(Module, "Primeiros passos",
            [
                Select with { Exercise = exercise },
                new("filtrando-com-where", "Filtrando com WHERE", "# rascunho", IsDraft: true),
            ]),
        ]));

    private static ExerciseDto SampleExercise() => new(
        "Primeiro SELECT", "Liste os títulos.", "sql", "livraria", "SELECT titulo FROM livros;",
        ["Use SELECT"], 10) { DatasetDisplayName = "Livraria" };

    [Fact]
    public async Task Dashboard_sem_selos_omite_a_area_inteira()
    {
        var client = DashboardClient(new FakeStudentJourneyGateway(), new FakeStudentAchievementSource());
        await RegisterAndChooseAsync(client);

        var html = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();

        html.Should().Contain("data-total-points>Seus pontos: 0</p>");
        html.Should().NotContain(AchievementTexts.SectionHeading);
        html.Should().NotContain("data-achievements");
        html.Should().NotContain("0 selos");
        html.Should().NotContain("Selo desbloqueado");
    }

    [Fact]
    public async Task Dashboard_com_selo_mostra_titulo_e_data_junto_dos_pontos()
    {
        var client = DashboardClient(
            new FakeStudentJourneyGateway
            {
                Dashboard = _ => FakeStudentJourneyGateway.Samples.InProgress() with { TotalPoints = 30 }
            },
            new FakeStudentAchievementSource { Items = [FirstSelect] });
        await RegisterAndChooseAsync(client);

        var html = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();

        html.Should().Contain("data-total-points>Seus pontos: 30</p>");
        var pointsAt = html.IndexOf("data-total-points", StringComparison.Ordinal);
        var selosAt = html.IndexOf("data-achievements", StringComparison.Ordinal);
        pointsAt.Should().BePositive();
        selosAt.Should().BeGreaterThan(pointsAt, "Seus selos fica logo abaixo da linha de pontos");

        var section = Regex.Match(html, @"<section[^>]*data-achievements[^>]*>.*?</section>", RegexOptions.Singleline);
        section.Success.Should().BeTrue();
        section.Value.Should().Contain($"id=\"seus-selos-titulo\"").And.Contain($">{AchievementTexts.SectionHeading}</h2>");
        section.Value.Should().Contain($"aria-labelledby=\"seus-selos-titulo\"");
        section.Value.Should().Contain(AchievementTexts.FirstSelectTitle);
        section.Value.Should().Contain($"<time datetime=\"{AchievementTexts.FormatDateIso(EarnedAt)}\">{AchievementTexts.EarnedOn(EarnedAt)}</time>");
        section.Value.Should().Contain("Ganho em 11 de novembro de 2026");
        section.Value.Should().Contain("aria-hidden=\"true\">🏅</span>");
        section.Value.Should().Contain("aria-labelledby=\"selo-titulo-0 selo-data-0\"");
        section.Value.Should().NotContain("2026-11-11T").And.NotContain("Z</time>").And.NotContain("0 selos");
        html.Should().NotContain("Selo desbloqueado", "o aviso de desbloqueio não aparece no dashboard (backfill silencioso)");
    }

    [Fact]
    public async Task Conclusao_do_primeiro_acerto_mostra_aviso_junto_dos_pontos()
    {
        await using var app = LessonApp(
            f =>
            {
                f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                    FakeLessonProgressGateway.Samples.ExercisePassedCompleted());
                return f;
            },
            new FakeStudentAchievementSource { NewlyUnlocked = FirstSelect });
        var client = await LoggedInAsync(app);

        var html = await (await client.GetAsync(ConclusaoUrl)).ReadHtmlAsync();

        html.Should().Contain(LessonCompletionTexts.PassCompletes(10));
        html.Should().Contain("+10 pontos");
        html.Should().Contain("data-announce=\"true\"");
        html.Should().Contain("data-completion-message");
        html.Should().MatchRegex(
            @"data-completion-message tabindex=""-1"">[\s\S]*data-achievement-unlocked[^>]*>\s*Selo desbloqueado: <strong>Primeiro SELECT</strong>\. Você fez um banco de dados responder à sua primeira pergunta\.\s*</span>\s*</span>\s*<span aria-hidden=""true"">🏅</span>",
            "o aviso entra no bloco anunciado; o 🏅 fica aria-hidden do lado de fora");
        var announced = Regex.Match(html,
            @"data-completion-message tabindex=""-1"">(?<body>[\s\S]*?)</span>\s*<span aria-hidden=""true"">🏅</span>");
        announced.Success.Should().BeTrue();
        AnnouncedText(announced.Groups["body"].Value).Should().Contain(LessonCompletionTexts.PassCompletes(10))
            .And.Contain(AchievementTexts.FirstSelectUnlockNotice)
            .And.NotContain("🏅");
    }

    [Fact]
    public async Task Conclusao_sem_selo_novo_nao_mostra_aviso()
    {
        await using var app = LessonApp(f =>
        {
            f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                FakeLessonProgressGateway.Samples.ExercisePassedCompleted());
            return f;
        });
        var client = await LoggedInAsync(app);

        var html = await (await client.GetAsync(ConclusaoUrl)).ReadHtmlAsync();

        html.Should().Contain(LessonCompletionTexts.PassCompletes(10));
        html.Should().NotContain("data-achievement-unlocked");
        html.Should().NotContain("Selo desbloqueado");
        html.Should().NotContain(AchievementTexts.FirstSelectTitle);
    }

    [Fact]
    public async Task Seguir_sem_pontos_nao_mostra_aviso_mesmo_com_gancho()
    {
        LessonProgressView? current = FakeLessonProgressGateway.Samples.ExerciseNotPassed(5, canSkip: true);
        await using var app = LessonApp(
            f =>
            {
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(current!);
                f.Skip = (_, _, _, _) =>
                {
                    current = FakeLessonProgressGateway.Samples.Skipped();
                    return new LessonCompletionOutcome(LessonCompletionStatus.Completed, current);
                };
                return f;
            },
            new FakeStudentAchievementSource { NewlyUnlocked = FirstSelect },
            SampleExercise());
        var client = await LoggedInAsync(app);

        var response = await client.SubmitFormAsync(LessonUrl, CompleteUrl, new Dictionary<string, string>
        {
            ["skipWithoutPoints"] = "true"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        html.Should().Contain(LessonCompletionTexts.SkipJustCompleted);
        html.Should().NotContain("data-achievement-unlocked");
        html.Should().NotContain("Selo desbloqueado");
    }

    [Fact]
    public async Task Tentativas_esgotadas_e_reabrir_nao_mostram_aviso()
    {
        var done = FakeLessonProgressGateway.Samples.ExerciseAttemptsExhaustedCompleted();
        await using var app = LessonApp(
            f =>
            {
                f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(done);
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(done);
                return f;
            },
            new FakeStudentAchievementSource { NewlyUnlocked = FirstSelect },
            SampleExercise());
        var client = await LoggedInAsync(app);

        var conclusao = await (await client.GetAsync(ConclusaoUrl)).ReadHtmlAsync();
        conclusao.Should().Contain(LessonCompletionTexts.AttemptsExhaustedJustCompleted);
        conclusao.Should().NotContain("data-achievement-unlocked");
        conclusao.Should().NotContain("Selo desbloqueado");

        var reopen = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        reopen.Should().Contain(LessonCompletionTexts.AttemptsExhaustedReopen);
        reopen.Should().NotContain("data-achievement-unlocked");
        reopen.Should().NotContain("Selo desbloqueado");
    }

    [Fact]
    public async Task Reabrir_aula_ja_acertada_nao_mostra_aviso()
    {
        await using var app = LessonApp(
            f =>
            {
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                    FakeLessonProgressGateway.Samples.ExercisePassedCompleted());
                return f;
            },
            new FakeStudentAchievementSource { NewlyUnlocked = FirstSelect, Items = [FirstSelect] },
            SampleExercise());
        var client = await LoggedInAsync(app);

        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        html.Should().Contain(LessonCompletionTexts.ReopenPassed(10));
        html.Should().NotContain("data-achievement-unlocked");
        html.Should().NotContain("Selo desbloqueado");
    }

    [Fact]
    public async Task Conclusao_primeiro_acerto_na_ultima_aula_depois_de_seguir_sem_pontos_anuncia_selo_junto_do_titulo_do_modulo()
    {
        const string last = "filtrando-com-where";
        var selectRef = new LessonRef(Path, Module, Lesson, "Seu primeiro SELECT");
        var lastRef = new LessonRef(Path, Module, last, "Filtrando com WHERE");
        var dashboard = new DashboardViewModel(Path, "SQL & Dados", 2, 2, 100, null, false,
        [
            new(Module, "Primeiros passos", 2, 2, 100,
            [
                new(selectRef, LessonStatus.Completed, Points: 0),
                new(lastRef, LessonStatus.Completed, Points: 10),
            ]),
        ], 10);
        var passed = FakeLessonProgressGateway.Samples.ExercisePassedCompleted() with
        {
            Lesson = lastRef,
            PathProgressPercentage = 100,
            Next = null,
        };

        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            var fake = new FakeLessonProgressGateway
            {
                GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(passed),
            };
            var lessons = new InMemoryLessonContentService(
                new InMemoryLessonContentService.FakePath(Path, "SQL & Dados",
                [
                    new(Module, "Primeiros passos",
                    [
                        Select with { Exercise = SampleExercise() },
                        new(last, "Filtrando com WHERE", "# WHERE\n## Resumo\nFeito.", EstimatedMinutes: 15,
                            Objectives: ["Filtrar"], Exercise: SampleExercise()),
                    ]),
                ]));
            services.Replace(ServiceDescriptor.Scoped<ILessonContentService>(_ => lessons));
            services.Replace(ServiceDescriptor.Scoped<ILessonProgressGateway>(_ => fake));
            services.RemoveAll<IStudentJourneyGateway>();
            services.AddSingleton<IStudentJourneyGateway>(new FixedJourney(dashboard));
            services.RemoveAll<IStudentAchievementSource>();
            services.AddSingleton<IStudentAchievementSource>(new FakeStudentAchievementSource { NewlyUnlocked = FirstSelect });
        }));
        var client = await LoggedInAsync(app);

        var html = await (await client.GetAsync($"/trilhas/{Path}/{Module}/{last}/conclusao")).ReadHtmlAsync();

        html.Should().Contain("data-module-completed");
        html.Should().Contain("data-announce=\"true\"");
        html.Should().NotContain("data-just-completed", "o pass do Executar só anuncia, sem mover o foco");
        html.Should().MatchRegex(
            @"data-completion-message tabindex=""-1"">Módulo concluído: Primeiros passos<span class=""sr-only"">\. </span><span[^>]*data-achievement-unlocked[^>]*>Selo desbloqueado: <strong>Primeiro SELECT</strong>\. Você fez um banco de dados responder à sua primeira pergunta\.</span></span>\s*<span aria-hidden=""true"">🎉</span>\s*<span aria-hidden=""true"">🏅</span>",
            "o aviso entra no mesmo bloco anunciado que o título do módulo; os emojis ficam fora");
        var announced = Regex.Match(html,
            @"data-completion-message tabindex=""-1"">(?<body>[\s\S]*?)</span>\s*<span aria-hidden=""true"">🎉</span>");
        announced.Success.Should().BeTrue();
        // textContent de verdade (o que o site.js anuncia): tags somem sem inserir espaço, então o separador
        // tem de estar no próprio texto. Sem ele o leitor ouviria "…passosSelo desbloqueado…".
        var textContent = TextContent(announced.Groups["body"].Value);
        textContent.Should().Be(
            $"{ModuleCompletionTexts.Title("Primeiros passos")}. {AchievementTexts.FirstSelectUnlockNotice}",
            "título do módulo e aviso do selo são duas frases, separadas por ponto e espaço");
        textContent.Should().Contain("Módulo concluído: Primeiros passos")
            .And.Contain(AchievementTexts.FirstSelectUnlockNotice)
            .And.NotContain("passosSelo")
            .And.NotMatchRegex(@"\p{L}Selo desbloqueado")
            .And.NotContain("🏅")
            .And.NotContain("🎉");
    }

    private static string AnnouncedText(string markup) =>
        Regex.Replace(TextContent(markup), @"\s+", " ").Trim();

    /// <summary>Como <c>Node.textContent.trim()</c>: tira as tags sem pôr nada no lugar e decodifica entidades.</summary>
    private static string TextContent(string markup) =>
        WebUtility.HtmlDecode(Regex.Replace(markup, "<[^>]+>", "")).Trim();

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

    private HttpClient DashboardClient(IStudentJourneyGateway journey, IStudentAchievementSource achievements) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStudentJourneyGateway>();
                services.AddSingleton(journey);
                services.RemoveAll<IStudentAchievementSource>();
                services.AddSingleton(achievements);
            }))
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
                BaseAddress = BaseAddress
            });

    private WebApplicationFactory<Program> LessonApp(
        Func<FakeLessonProgressGateway, FakeLessonProgressGateway>? configure = null,
        IStudentAchievementSource? achievements = null,
        ExerciseDto? exercise = null)
    {
        var fake = new FakeLessonProgressGateway();
        configure?.Invoke(fake);
        return factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Scoped<ILessonContentService>(_ => Lessons(exercise ?? SampleExercise())));
            services.Replace(ServiceDescriptor.Scoped<ILessonProgressGateway>(_ => fake));
            services.RemoveAll<IStudentAchievementSource>();
            services.AddSingleton(achievements ?? new FakeStudentAchievementSource());
        }));
    }

    private static async Task<HttpClient> LoggedInAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true, BaseAddress = BaseAddress
        });
        var response = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Aluna Selo",
            ["Email"] = $"selo-{Guid.NewGuid():N}@devpath.test",
            ["Password"] = Password,
            ["ConfirmPassword"] = Password,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private static async Task RegisterAndChooseAsync(HttpClient client)
    {
        var register = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Aluna Selo",
            ["Email"] = $"selo-{Guid.NewGuid():N}@devpath.test",
            ["Password"] = Password,
            ["ConfirmPassword"] = Password,
        });
        register.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var choose = await client.SubmitFormAsync("/escolha-sua-trilha", "/escolha-sua-trilha",
            new Dictionary<string, string> { ["pathSlug"] = FakeStudentJourneyGateway.SqlPath });
        choose.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }
}
