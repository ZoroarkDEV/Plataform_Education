using System.Net;
using DevPath.Application.Lessons;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.IntegrationTests.Lessons;
using DevPath.Web.Models.Lessons;
using DevPath.Web.Progress;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevPath.IntegrationTests.Progress;

/// <summary>Tela de conclusão (#27) + textos do Executar (#30 B2) com FakeLessonProgressGateway.</summary>
[Collection(WebAppCollection.Name)]
public sealed class LessonCompletionPageTests(DevPathWebApplicationFactory factory)
{
    private const string Password = "Senha123";
    private const string Path = "sql-dados";
    private const string Module = "primeiros-passos";
    private const string Lesson = "seu-primeiro-select";
    private static string LessonUrl => $"/trilhas/{Path}/{Module}/{Lesson}";
    private static string CompleteUrl => $"{LessonUrl}/concluir";
    private static string ConclusaoUrl => $"{LessonUrl}/conclusao";
    private static readonly Uri BaseAddress = new("https://localhost");

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

    private WebApplicationFactory<Program> App(
        Func<FakeLessonProgressGateway, FakeLessonProgressGateway>? configure = null,
        ExerciseDto? exercise = null)
    {
        var fake = new FakeLessonProgressGateway();
        configure?.Invoke(fake);
        return factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Scoped<ILessonContentService>(_ => Lessons(exercise)));
            services.Replace(ServiceDescriptor.Scoped<ILessonProgressGateway>(_ => fake));
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
            ["DisplayName"] = "Aluna Progresso",
            ["Email"] = $"prog-{Guid.NewGuid():N}@devpath.test",
            ["Password"] = Password,
            ["ConfirmPassword"] = Password,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private static ExerciseDto SampleExercise(IReadOnlyList<string>? hints = null) => new(
        "Primeiro SELECT", "Liste os títulos.", "sql", "livraria", "SELECT titulo FROM livros;",
        hints ?? ["Use SELECT"], 10) { DatasetDisplayName = "Livraria" };

    [Fact]
    public async Task Pending_gateway_esconde_botao_e_nao_quebra()
    {
        await using var app = App(); // default Unavailable
        var client = await LoggedInAsync(app);
        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();

        html.Should().NotContain(LessonCompletionTexts.CompleteButton);
        html.Should().NotContain("data-complete-button");
        html.Should().NotContain("id=\"lesson-completion\"");
        html.Should().Contain("Seu primeiro SELECT");
    }

    [Fact]
    public async Task Reading_available_mostra_botao_e_proxima_travada()
    {
        await using var app = App(f =>
        {
            var nextLocked = FakeLessonProgressGateway.Samples.Locked(
                FakeLessonProgressGateway.Samples.Where, FakeLessonProgressGateway.Samples.Select);
            f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                FakeLessonProgressGateway.Samples.ReadingAvailable(nextLocked));
            return f;
        });
        var client = await LoggedInAsync(app);
        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();

        html.Should().Contain(LessonCompletionTexts.CompleteButton);
        html.Should().Contain(LessonCompletionTexts.NextLockedReason);
        html.Should().Contain("aria-disabled");
        html.Should().Contain("id=\"lesson-live\"");
        html.Should().Contain("aria-live=\"polite\"");
        html.Should().NotContain("faltam");
        html.Should().NotContain("tentativas esgotadas");
    }

    [Fact]
    public async Task Post_concluir_sem_htmx_prg_com_flash()
    {
        LessonProgressView? current = FakeLessonProgressGateway.Samples.ReadingAvailable();
        await using var app = App(f =>
        {
            f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(current!);
            f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(current!);
            f.Complete = (_, _, _, _) =>
            {
                current = FakeLessonProgressGateway.Samples.ReadingCompleted();
                return new LessonCompletionOutcome(LessonCompletionStatus.Completed, current);
            };
            return f;
        });
        var client = await LoggedInAsync(app);

        var response = await client.SubmitFormAsync(LessonUrl, CompleteUrl, new Dictionary<string, string>());
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(LessonUrl);

        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        html.Should().Contain(LessonCompletionTexts.BadgeVisible);
        // Flash do PRG tem prioridade na 1ª carga; depois vale o texto de reabrir.
        html.Should().ContainAny(LessonCompletionTexts.SuccessGeneric, LessonCompletionTexts.ReopenReading);
        html.Should().NotContain("data-complete-button>");
    }

    [Fact]
    public async Task Post_concluir_htmx_devolve_partial_e_trigger()
    {
        LessonProgressView? current = FakeLessonProgressGateway.Samples.ReadingAvailable();
        await using var app = App(f =>
        {
            f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(current!);
            f.Complete = (_, _, _, _) =>
            {
                current = FakeLessonProgressGateway.Samples.ReadingCompleted();
                return new LessonCompletionOutcome(LessonCompletionStatus.Completed, current);
            };
            return f;
        });
        var client = await LoggedInAsync(app);
        // Token da página
        var page = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        var token = System.Text.RegularExpressions.Regex.Match(
            page, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""").Groups[1].Value;

        using var req = new HttpRequestMessage(HttpMethod.Post, CompleteUrl);
        req.Headers.Add("HX-Request", "true");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
        });
        var response = await client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("HX-Trigger");
        var body = await response.ReadHtmlAsync();
        body.Should().Contain(LessonCompletionTexts.BadgeVisible);
        body.Should().Contain(LessonCompletionTexts.SuccessGeneric);
        body.Should().Contain("hx-swap-oob");
    }

    [Fact]
    public async Task Post_sem_antiforgery_falha()
    {
        await using var app = App(f =>
        {
            f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                FakeLessonProgressGateway.Samples.ReadingAvailable());
            return f;
        });
        var client = await LoggedInAsync(app);
        var response = await client.PostAsync(CompleteUrl, new FormUrlEncodedContent(new Dictionary<string, string>()));
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, (HttpStatusCode)400);
        // Antiforgery do ASP.NET Core: 400
        ((int)response.StatusCode).Should().Be(400);
    }

    [Fact]
    public async Task Double_submit_idempotente()
    {
        var completed = FakeLessonProgressGateway.Samples.ReadingCompleted();
        var calls = 0;
        await using var app = App(f =>
        {
            f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                calls == 0 ? FakeLessonProgressGateway.Samples.ReadingAvailable() : completed);
            f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(completed);
            f.Complete = (_, _, _, _) =>
            {
                calls++;
                return new LessonCompletionOutcome(
                    calls == 1 ? LessonCompletionStatus.Completed : LessonCompletionStatus.AlreadyCompleted,
                    completed);
            };
            return f;
        });
        var client = await LoggedInAsync(app);
        (await client.SubmitFormAsync(LessonUrl, CompleteUrl, new Dictionary<string, string>()))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);
        var after1 = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        after1.Should().Contain(LessonCompletionTexts.BadgeVisible);
        after1.Should().NotContain("+0");

        (await client.SubmitFormAsync(LessonUrl, CompleteUrl, new Dictionary<string, string>()))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);
        var after2 = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        after2.Should().Contain(LessonCompletionTexts.BadgeVisible);
        after2.Should().NotContain("+0");
        // Mesmo estado/texto de reabrir (2º POST AlreadyCompleted, sem flash novo de sucesso).
        after2.Should().Contain(LessonCompletionTexts.ReopenReading);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Double_submit_concluir_com_pontos_mantem_mesmo_estado()
    {
        var completed = FakeLessonProgressGateway.Samples.ExercisePassedCompleted(10);
        var calls = 0;
        await using var app = App(f =>
        {
            f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                calls == 0
                    ? FakeLessonProgressGateway.Samples.ExerciseNotPassed() with { CanComplete = true, Blocker = null }
                    : completed);
            f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(completed);
            f.Complete = (_, _, _, _) =>
            {
                calls++;
                return new LessonCompletionOutcome(
                    calls == 1 ? LessonCompletionStatus.Completed : LessonCompletionStatus.AlreadyCompleted,
                    completed);
            };
            return f;
        }, SampleExercise());
        var client = await LoggedInAsync(app);

        (await client.SubmitFormAsync(LessonUrl, CompleteUrl, new Dictionary<string, string>()))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);
        var after1 = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        after1.Should().Contain(LessonCompletionTexts.BadgeVisible);
        after1.Should().Contain("+10 pontos");

        (await client.SubmitFormAsync(LessonUrl, CompleteUrl, new Dictionary<string, string>()))
            .StatusCode.Should().Be(HttpStatusCode.Redirect);
        var after2 = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        after2.Should().Contain(LessonCompletionTexts.BadgeVisible);
        after2.Should().Contain("+10 pontos");
        after2.Should().NotContain("+0");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Double_submit_seguir_sem_pontos_mantem_mesmo_estado()
    {
        var skipped = FakeLessonProgressGateway.Samples.Skipped(10);
        var calls = 0;
        await using var app = App(f =>
        {
            f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                calls == 0
                    ? FakeLessonProgressGateway.Samples.ExerciseNotPassed(5, canSkip: true)
                    : skipped);
            f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(skipped);
            f.Skip = (_, _, _, _) =>
            {
                calls++;
                return new LessonCompletionOutcome(
                    calls == 1 ? LessonCompletionStatus.Completed : LessonCompletionStatus.AlreadyCompleted,
                    skipped);
            };
            return f;
        }, SampleExercise());
        var client = await LoggedInAsync(app);
        var body = new Dictionary<string, string> { ["skipWithoutPoints"] = "true" };

        (await client.SubmitFormAsync(LessonUrl, CompleteUrl, body)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        var after1 = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        after1.Should().Contain(LessonCompletionTexts.BadgeVisible);
        after1.Should().ContainAny(LessonCompletionTexts.SkipJustCompleted, "seguiu sem pontos");
        after1.Should().NotContain("+0");
        // Consome o flash do 1º POST; o reopen mostra os pontos ainda a ganhar.
        var after1b = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        after1b.Should().Contain("+10 pontos");

        (await client.SubmitFormAsync(LessonUrl, CompleteUrl, body)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        var after2 = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        after2.Should().Contain(LessonCompletionTexts.BadgeVisible);
        after2.Should().NotContain("+0");
        // AlreadyCompleted não cria flash novo: mesmo reopen / mesmos pontos a ganhar.
        var after2b = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        after2b.Should().Contain("+10 pontos");
        after2b.Should().Contain("seguiu sem pontos");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Locked_devolve_403_com_motivo()
    {
        var locked = FakeLessonProgressGateway.Samples.ReadingAvailable() with
        {
            Status = DevPath.Web.ViewModels.Onboarding.LessonStatus.Locked,
            CanComplete = false,
            UnlockRequirement = FakeLessonProgressGateway.Samples.Select with { Title = "Introdução" },
        };
        await using var app = App(f =>
        {
            f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.LockedState(locked);
            f.GetState = (_, _, _, _) => new LessonAccessOutcome(LessonAccessStatus.Locked, locked);
            return f;
        });
        var client = await LoggedInAsync(app);
        var response = await client.GetAsync(LessonUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var html = await response.ReadHtmlAsync();
        html.Should().Contain(LessonCompletionTexts.LockedTitle);
        html.Should().Contain("Introdução");
        html.Should().Contain("data-lesson-unavailable=\"Locked\"");
    }

    [Fact]
    public async Task NotEnrolled_sem_trilha_redireciona_escolha()
    {
        await using var app = App(f =>
        {
            f.Open = (_, _, _, _) => new LessonAccessOutcome(LessonAccessStatus.NotEnrolled);
            return f;
        });
        var client = await LoggedInAsync(app);
        var response = await client.GetAsync(LessonUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("escolha-sua-trilha");
    }

    [Fact]
    public async Task OtherPath_devolve_403()
    {
        await using var app = App(f =>
        {
            f.Open = (_, _, _, _) => new LessonAccessOutcome(LessonAccessStatus.NotEnrolled, CurrentPathTitle: "Python");
            return f;
        });
        var client = await LoggedInAsync(app);
        var response = await client.GetAsync(LessonUrl);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var html = await response.ReadHtmlAsync();
        html.Should().Contain(LessonCompletionTexts.OtherPathTitle);
        html.Should().Contain("Python");
    }

    [Fact]
    public async Task Exercise_sem_pass_mostra_botao_travado_e_dataset_legivel()
    {
        await using var app = App(
            f =>
            {
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                    FakeLessonProgressGateway.Samples.ExerciseNotPassed(failed: 1));
                return f;
            },
            SampleExercise());
        var client = await LoggedInAsync(app);
        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();

        html.Should().Contain(LessonCompletionTexts.ExerciseBlocker);
        html.Should().Contain("data-complete-button-locked");
        html.Should().Contain("data-dataset-name");
        html.Should().Contain(">Livraria<");
        html.Should().NotContain(">livraria<");
        html.Should().Contain(LessonCompletionTexts.ExecuteButton);
        html.Should().Contain("Pode executar quantas vezes quiser");
        html.Should().NotContain("Copie e complete no seu editor");
        html.Should().NotContain("Seguir sem pontos");
    }

    [Fact]
    public async Task CanSkip_mostra_seguir_sem_pontos_sem_contador()
    {
        await using var app = App(
            f =>
            {
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                    FakeLessonProgressGateway.Samples.ExerciseNotPassed(failed: 5, canSkip: true));
                return f;
            },
            SampleExercise());
        var client = await LoggedInAsync(app);
        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();

        html.Should().Contain(LessonCompletionTexts.SkipButton);
        html.Should().Contain(LessonCompletionTexts.SkipDescribedBy);
        html.Should().NotContain("faltam");
        html.Should().NotContain("Você usou todas as tentativas, então esta aula");
    }

    [Theory]
    [InlineData(2, 0, "2")]
    [InlineData(7, 3, "4")]
    [InlineData(5, 5, "0")]
    public async Task Pagina_manda_ao_js_as_falhas_que_faltam_do_limite_do_exercicio(int limit, int failed, string expected)
    {
        await using var app = App(
            f =>
            {
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                    FakeLessonProgressGateway.Samples.ExerciseNotPassed(failed, canSkip: failed >= limit, failuresBeforeSkip: limit));
                return f;
            },
            SampleExercise());
        var client = await LoggedInAsync(app);
        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();

        html.Should().Contain($"data-failures-until-skip=\"{expected}\"", "o site.js anuncia o pulo pelo limite real (#29)");
        html.Should().NotContain("faltam", "a contagem não aparece na tela (UX)");
    }

    [Fact]
    public async Task Skip_post_conclui_sem_pontos()
    {
        LessonProgressView? current = FakeLessonProgressGateway.Samples.ExerciseNotPassed(5, canSkip: true);
        FakeLessonProgressGateway? fake = null;
        await using var app = App(
            f =>
            {
                fake = f;
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(current!);
                f.Skip = (_, _, _, _) =>
                {
                    current = FakeLessonProgressGateway.Samples.Skipped();
                    return new LessonCompletionOutcome(LessonCompletionStatus.Completed, current);
                };
                return f;
            },
            SampleExercise());
        var client = await LoggedInAsync(app);
        var response = await client.SubmitFormAsync(LessonUrl, CompleteUrl,
            new Dictionary<string, string> { ["skipWithoutPoints"] = "true" });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        fake!.SkipCalls.Should().Be(1, "Seguir sem pontos vai para SkipAsync");
        fake.CompleteCalls.Should().Be(0, "CompleteAsync não deve ser chamado no pulo");

        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        html.Should().Contain(LessonCompletionTexts.BadgeVisible);
        html.Should().ContainAny(
            LessonCompletionTexts.SkipJustCompleted,
            "seguiu sem pontos");
        html.Should().NotContain("+0");
        // Segunda carga (sem flash): texto de reabrir com pontos a ganhar.
        var html2 = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        html2.Should().Contain("seguiu sem pontos");
        html2.Should().Contain("+10 pontos");
    }

    [Fact]
    public async Task Conclui_com_CanSkip_sem_pass_mostra_bloqueio_nao_pula()
    {
        // "Concluí" com CanSkip e sem pass: RequirementNotMet (bloqueio), nunca skip.
        var state = FakeLessonProgressGateway.Samples.ExerciseNotPassed(5, canSkip: true);
        FakeLessonProgressGateway? fake = null;
        await using var app = App(
            f =>
            {
                fake = f;
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(state);
                f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(state);
                f.Complete = (_, _, _, _) => new LessonCompletionOutcome(LessonCompletionStatus.RequirementNotMet, state);
                return f;
            },
            SampleExercise());
        var client = await LoggedInAsync(app);
        var response = await client.SubmitFormAsync(LessonUrl, CompleteUrl, new Dictionary<string, string>());
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        fake!.CompleteCalls.Should().Be(1);
        fake.SkipCalls.Should().Be(0, "Concluí não chama SkipAsync mesmo com CanSkip");

        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        html.Should().Contain(LessonCompletionTexts.RequirementNotMetFlash);
        html.Should().Contain(LessonCompletionTexts.ExerciseBlocker);
        html.Should().Contain(LessonCompletionTexts.SkipButton, "o Seguir sem pontos continua disponível");
        html.Should().NotContain(LessonCompletionTexts.BadgeVisible);
    }

    [Fact]
    public async Task Get_conclusao_atualiza_bloco()
    {
        await using var app = App(f =>
        {
            f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                FakeLessonProgressGateway.Samples.ExercisePassedCompleted());
            return f;
        }, SampleExercise());
        var client = await LoggedInAsync(app);
        var response = await client.GetAsync(ConclusaoUrl);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.ReadHtmlAsync();
        html.Should().Contain(LessonCompletionTexts.BadgeVisible);
        html.Should().Contain("+10 pontos");
        html.Should().Contain("hx-swap-oob");
    }

    [Fact]
    public async Task Get_conclusao_depois_do_pass_so_anuncia_sem_mover_foco()
    {
        await using var app = App(f =>
        {
            f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                FakeLessonProgressGateway.Samples.ExercisePassedCompleted());
            return f;
        }, SampleExercise());
        var client = await LoggedInAsync(app);
        var html = await (await client.GetAsync($"{ConclusaoUrl}?antes=SkippedAfterFailures")).ReadHtmlAsync();
        html.Should().NotContain("data-just-completed");
        html.Should().NotContain("data-focus-after-complete");
        html.Should().Contain("data-announce=\"true\"");
        html.Should().Contain("Boa! Você acertou o exercício e ganhou +10 pontos.");

        var plain = await (await client.GetAsync($"{ConclusaoUrl}?antes=xyz")).ReadHtmlAsync();
        plain.Should().Contain(LessonCompletionTexts.PassCompletes(10));
        plain.Should().NotContain("data-just-completed");
    }

    [Fact]
    public async Task Attempts_limited_mostra_contador()
    {
        await using var app = App(
            f =>
            {
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                    FakeLessonProgressGateway.Samples.ExerciseNotPassed(failed: 1, maxAttempts: 3, remaining: 2));
                return f;
            },
            SampleExercise());
        var client = await LoggedInAsync(app);
        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        html.Should().Contain("Cada execução conta como tentativa");
        html.Should().Contain("Tentativas restantes: 2");
    }

    [Fact]
    public async Task Attempts_exhausted_mostra_concluir_trava_executar_e_proxima()
    {
        var ready = FakeLessonProgressGateway.Samples.ExerciseAttemptsExhaustedReady();
        await using var app = App(
            f =>
            {
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(ready);
                f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(ready);
                return f;
            },
            SampleExercise());
        var client = await LoggedInAsync(app);
        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        html.Should().Contain(LessonCompletionTexts.AttemptsExhaustedReason);
        html.Should().Contain("data-complete-form");
        html.Should().Contain(LessonCompletionTexts.CompleteButton);
        html.Should().NotContain("data-skip-form", "esgotar não libera Seguir; CanSkip=false");
        html.Should().Contain("data-execute-button");
        html.Should().Contain("aria-disabled");
        html.Should().NotContain("href=\"/trilhas/" + Path + "/" + Module + "/filtrando-com-where\"");

        var refresh = await (await client.GetAsync(ConclusaoUrl)).ReadHtmlAsync();
        refresh.Should().Contain("data-complete-form", "GET /conclusao após remainingAttempts=0 traz o botão");
        refresh.Should().Contain(LessonCompletionTexts.CompleteButton);
    }

    [Fact]
    public async Task Attempts_exhausted_concluir_libera_proxima_e_persiste_no_reload()
    {
        var ready = FakeLessonProgressGateway.Samples.ExerciseAttemptsExhaustedReady();
        var done = FakeLessonProgressGateway.Samples.ExerciseAttemptsExhaustedCompleted();
        var calls = 0;
        await using var app = App(
            f =>
            {
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(calls == 0 ? ready : done);
                f.GetState = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(done);
                f.Complete = (_, _, _, _) =>
                {
                    calls++;
                    return new LessonCompletionOutcome(LessonCompletionStatus.Completed, done);
                };
                return f;
            },
            SampleExercise());
        var client = await LoggedInAsync(app);

        (await client.GetAsync(LessonUrl)).EnsureSuccessStatusCode();
        var post = await client.SubmitFormAsync(LessonUrl, CompleteUrl, new Dictionary<string, string>());
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var after = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        after.Should().Contain(LessonCompletionTexts.BadgeVisible);
        after.Should().ContainAny(
            LessonCompletionTexts.AttemptsExhaustedJustCompleted,
            LessonCompletionTexts.AttemptsExhaustedReopen);
        after.Should().Contain("href=\"/trilhas/" + Path + "/" + Module + "/filtrando-com-where\"");
        after.Should().NotContain("+0");

        var reload = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        reload.Should().Contain(LessonCompletionTexts.BadgeVisible);
        reload.Should().Contain(LessonCompletionTexts.AttemptsExhaustedReopen);
        reload.Should().Contain("href=\"/trilhas/" + Path + "/" + Module + "/filtrando-com-where\"");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Sem_dicas_some_frase_das_dicas()
    {
        await using var app = App(
            f =>
            {
                f.Open = (_, _, _, _) => FakeLessonProgressGateway.Samples.Allowed(
                    FakeLessonProgressGateway.Samples.ExerciseNotPassed());
                return f;
            },
            SampleExercise(hints: Array.Empty<string>()));
        var client = await LoggedInAsync(app);
        var html = await (await client.GetAsync(LessonUrl)).ReadHtmlAsync();
        html.Should().Contain("Pode executar quantas vezes quiser");
        html.Should().NotContain("abra as dicas");
    }
}
