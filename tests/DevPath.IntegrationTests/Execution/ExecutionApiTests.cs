using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevPath.Application.CodeExecution;
using DevPath.Application.Journey;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.CodeExecution;
using DevPath.Infrastructure.Persistence;
using DevPath.IntegrationTests.CodeExecution;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.Web.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DevPath.IntegrationTests.Execution;

/// <summary>
/// <c>POST /api/exercicios/{id}/executar</c> de ponta a ponta: HTTP → Web → Redis → Worker → sandbox → resposta.
/// </summary>
/// <remarks>
/// Fica na coleção do sandbox para não rodar junto com os testes que contam containers de sandbox.
/// Cada teste usa um aluno novo, para o limite por aluno de um teste não atrapalhar o outro.
/// </remarks>
[Collection(SandboxCollection.Name)]
public sealed class ExecutionApiTests(ExecutionApiFactory factory) : IClassFixture<ExecutionApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string ExecuteUrl(Guid exerciseId) => $"/api/exercicios/{exerciseId}/executar";

    // ---------- Segurança e acesso (não precisam do Worker) ----------

    [Fact]
    public async Task SemLogin_Devolve401SemRedirect()
    {
        var client = factory.CreateBrowserClient();

        var response = await client.PostAsJsonAsync(ExecuteUrl(factory.ExerciseId), new { code = "SELECT 1;" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull("o fetch do Frontend não deve seguir para a tela de login");
    }

    [Fact]
    public async Task PaginasNormais_ContinuamRedirecionandoParaLogin()
    {
        var response = await factory.CreateBrowserClient().GetAsync("/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task SemTokenAntiforgery_Devolve400()
    {
        var client = await LoggedInClientAsync(withAntiforgery: false);

        var response = await client.PostAsJsonAsync(ExecuteUrl(factory.ExerciseId), new { code = "SELECT 1;" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ErrorAsync(response);
        error.Message.Should().Be(ExerciseExecutionEndpoints.AntiforgeryMessage);
        error.Code.Should().Be(ExecutionApiErrorCodes.AntiforgeryInvalid);
    }

    [Fact]
    public async Task EndpointDoToken_DevolveTokenENomeDoHeader()
    {
        var client = await LoggedInClientAsync(withAntiforgery: false);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery/token");

        body.GetProperty("token").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("headerName").GetString().Should().Be("RequestVerificationToken");
    }

    [Fact]
    public async Task CodigoVazio_Devolve400ComMensagem()
    {
        var client = await LoggedInClientAsync();

        var response = await client.PostAsJsonAsync(ExecuteUrl(factory.ExerciseId), new { code = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ErrorAsync(response);
        error.Message.Should().Be(ExerciseExecutionEndpoints.EmptyCodeMessage);
        error.Code.Should().Be(ExecutionApiErrorCodes.CodeEmpty);
    }

    [Fact]
    public async Task JsonMalformado_Devolve400ComCodeMalformedBody()
    {
        var client = await LoggedInClientAsync();
        using var content = new StringContent("{ code: ", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync(ExecuteUrl(factory.ExerciseId), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ErrorAsync(response);
        error.Code.Should().Be(ExecutionApiErrorCodes.MalformedBody);
        error.Message.Should().Be(ExerciseExecutionEndpoints.MalformedBodyMessage);
    }

    [Fact]
    public async Task CodigoGrandeDemais_Devolve400ComCodeTooLong()
    {
        var client = await LoggedInClientAsync();
        var code = new string('a', ExecutionLimits.MaxCodeLength + 1);

        var response = await client.PostAsJsonAsync(ExecuteUrl(factory.ExerciseId), new { code });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorAsync(response)).Code.Should().Be(ExecutionApiErrorCodes.CodeTooLong);
    }

    [Fact]
    public async Task AulaEmRascunho_Devolve404IgualAoInexistente()
    {
        var client = await LoggedInClientAsync();

        var draft = await client.PostAsJsonAsync(ExecuteUrl(factory.DraftExerciseId), new { code = "SELECT 1;" });
        var missing = await client.PostAsJsonAsync(ExecuteUrl(Guid.NewGuid()), new { code = "SELECT 1;" });

        // Não revela que existe uma aula em construção: mesmo status e mesmo corpo do inexistente.
        draft.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await draft.Content.ReadAsStringAsync()).Should().Be(await missing.Content.ReadAsStringAsync());
        (await MessageAsync(draft)).Should().Be(ExerciseExecutionEndpoints.NotFoundMessage);
    }

    [Fact]
    public async Task ExercicioEscondidoOuInexistente_Devolve404()
    {
        var client = await LoggedInClientAsync();

        foreach (var id in new[] { factory.UnpublishedLessonExerciseId, factory.UnpublishedModuleExerciseId,
                     factory.InactivePathExerciseId, Guid.NewGuid() })
        {
            var response = await client.PostAsJsonAsync(ExecuteUrl(id), new { code = "SELECT 1;" });
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"exercício {id}");
            (await MessageAsync(response)).Should().Be(ExerciseExecutionEndpoints.NotFoundMessage);
        }
    }

    [Fact]
    public async Task MuitasExecucoesSeguidas_Devolve429EmPortuguesComRetryAfter()
    {
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:Execution:PermitLimit", "3"));
        var client = await LoggedInClientAsync(limited);

        // Exercício inexistente: responde 404 na hora, mas cada chamada conta no limite do mesmo jeito.
        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync(ExecuteUrl(Guid.NewGuid()), new { code = "SELECT 1;" }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        var blocked = await client.PostAsJsonAsync(ExecuteUrl(Guid.NewGuid()), new { code = "SELECT 1;" });

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.TryGetValues("Retry-After", out var retryAfter).Should().BeTrue();
        int.Parse(retryAfter!.Single()).Should().BePositive();
        var body = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Be(
            "Calma! Você enviou muitas execuções seguidas. Espere alguns segundos e tente de novo.");
        body.GetProperty("retryAfterSeconds").GetInt32().Should().Be(10, "um segmento da janela: 60 s / 6");
        retryAfter!.Single().Should().Be("10");

        // O limite é por aluno: outro aluno continua executando.
        var other = await LoggedInClientAsync(limited);
        (await other.PostAsJsonAsync(ExecuteUrl(Guid.NewGuid()), new { code = "SELECT 1;" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WorkerNaoResponde_Devolve503_ESegundaExecucaoSimultaneaDevolve429()
    {
        // Fila que nenhum Worker escuta + espera curta: simula o Worker parado.
        await using var noWorker = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Execution:QueueKey", $"devpath:teste:sem-worker:{Guid.NewGuid():N}");
            b.UseSetting("Execution:ResultTimeoutSeconds", "3");
        });
        var client = await LoggedInClientAsync(noWorker);

        var first = client.PostAsJsonAsync(ExecuteUrl(factory.ExerciseId), new { code = "SELECT 1;" });
        await Task.Delay(500);
        var second = await client.PostAsJsonAsync(ExecuteUrl(factory.ExerciseId), new { code = "SELECT 1;" });

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "só 1 execução por aluno por vez");
        second.Headers.GetValues("Retry-After").Single().Should().Be("2");
        (await MessageAsync(second)).Should().Be(ExecutionRateLimiting.ConcurrentMessage);
        var firstResponse = await first;
        firstResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await MessageAsync(firstResponse)).Should().Be(ExerciseExecutionEndpoints.UnavailableMessage);
    }

    [Fact]
    public void WebNaoTemSandboxNemDocker()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        services.GetService<ICodeSandbox>().Should().BeNull();
        services.GetService<ICodeRunner>().Should().BeNull();
        services.GetServices<ICodeRunner>().Should().BeEmpty();
        services.GetService<IExerciseSubmissionService>().Should().BeNull();
        services.GetService<IDatasetRepository>().Should().BeNull("só o Worker monta a preparação do sandbox");
        services.GetServices<IHostedService>().Should().NotContain(s => s is ExecutionWorker);
        services.GetService<IExecutionQueue>().Should().BeOfType<RedisExecutionQueue>();
    }

    // ---------- Execução de verdade (Worker + sandbox) ----------

    [DockerFact]
    public async Task SelectCorreto_DevolvePassComTabelaEGravaSubmissao()
    {
        var client = await LoggedInClientAsync();

        var outcome = await ExecuteAsync(client, factory.ExerciseId, "SELECT nome, nota FROM alunos ORDER BY nome;");

        outcome.Status.Should().Be(ExecutionStatus.Pass, outcome.FriendlyMessage + outcome.TechnicalDetail);
        outcome.TestCases.Should().ContainSingle().Which.Should().Be(new TestCaseFeedback("notas por nome", true, null));
        outcome.PassedCases.Should().Be(1);
        outcome.Columns.Should().Equal("nome", "nota");
        outcome.Rows.Should().HaveCount(3);
        outcome.Rows[0].Should().Equal("Ana", "9");
        outcome.Rows[2].Should().Equal("Caio", null);
        outcome.Truncated.Should().BeFalse();
        outcome.ExecutionTimeMs.Should().BePositive();
        outcome.SubmissionId.Should().NotBeNull();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        (await db.CodeSubmissions.SingleAsync(s => s.Id == outcome.SubmissionId)).ExerciseId.Should().Be(factory.ExerciseId);
    }

    [DockerFact]
    public async Task ErroComumSemeado_DevolveCommonErrorMessage_ESolucaoNaoVaza()
    {
        var client = await LoggedInClientAsync();

        var response = await client.PostAsJsonAsync(ExecuteUrl(factory.ExerciseId),
            new { code = "  SELECT   nme  FROM  alunos ; " });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("commonErrorMessage").GetString()
            .Should().Be("A coluna se chama nome, não nme. Confira a lista de colunas.");

        var raw = body.GetRawText();
        raw.Should().NotContain("SOLUCAO_SECRETA")
            .And.NotContain("\"example\"")
            .And.NotContain("common_errors")
            .And.NotContain("SELECT nme FROM alunos;"); // o example em si não volta
    }

    [DockerFact]
    public async Task SolucaoCorreta_CommonErrorMessageNulo()
    {
        var client = await LoggedInClientAsync();

        var outcome = await ExecuteAsync(client, factory.ExerciseId, "SELECT nome, nota FROM alunos ORDER BY nome;");

        outcome.Status.Should().Be(ExecutionStatus.Pass, outcome.FriendlyMessage);
        outcome.CommonErrorMessage.Should().BeNull();
    }

    [DockerFact]
    public async Task RespostaJson_UsaOContratoDoFrontend()
    {
        var client = await LoggedInClientAsync();

        var response = await client.PostAsJsonAsync(ExecuteUrl(factory.ExerciseId), new { code = "SELECT nome FROM alunos ORDER BY nome;" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("fail");
        foreach (var field in new[] { "columns", "rows", "truncated", "friendlyMessage", "technicalDetail", "executionTimeMs",
                     "passedCases", "totalCases", "testCases", "submissionId", "remainingAttempts", "commonErrorMessage", "lessonCompleted" })
        {
            body.TryGetProperty(field, out _).Should().BeTrue($"o campo {field} faz parte do contrato");
        }
        body.GetProperty("commonErrorMessage").GetString()
            .Should().Be("Faltou a coluna nota: o exercício pede nome e nota.");
        var testCase = body.GetProperty("testCases")[0];
        testCase.GetProperty("name").GetString().Should().Be("notas por nome");
        testCase.GetProperty("passed").GetBoolean().Should().BeFalse();
        testCase.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();

        // O retorno nunca entrega a resposta: nem as linhas esperadas (as notas) nem o formato do caso de teste.
        var raw = body.GetRawText();
        raw.Should().NotContain("\"9\"").And.NotContain("orderMatters").And.NotContain("expected").And.NotContain("solution")
            .And.NotContain("SOLUCAO_SECRETA").And.NotContain("common_errors");
    }

    [DockerTheory]
    [InlineData("CREATE TABLE notas_novas (id int);")]
    [InlineData("DROP TABLE alunos;")]
    [InlineData("INSERT INTO alunos VALUES ('Duda', 10);")]
    [InlineData("UPDATE alunos SET nota = 10;")]
    [InlineData("DELETE FROM alunos;")]
    [InlineData("TRUNCATE alunos;")]
    public async Task DdlEDml_SaoRecusadosComMensagemAmigavel(string sql)
    {
        var client = await LoggedInClientAsync();

        var outcome = await ExecuteAsync(client, factory.ExerciseId, sql);

        outcome.Status.Should().Be(ExecutionStatus.Error);
        outcome.FriendlyMessage.Should().Be(SqlErrorTranslator.ReadOnlyMessage);
        outcome.TechnicalDetail.Should().MatchRegex("42P11|25006|42501");
        outcome.SubmissionId.Should().NotBeNull("comando recusado conta como tentativa, como qualquer erro do aluno");
    }

    [DockerFact]
    public async Task VariosComandos_SaoRecusados_NemDaParaDesligarOReadOnly()
    {
        var client = await LoggedInClientAsync();

        var outcome = await ExecuteAsync(client, factory.ExerciseId,
            "SET default_transaction_read_only = off; DELETE FROM alunos;");

        outcome.Status.Should().Be(ExecutionStatus.Error);
        outcome.FriendlyMessage.Should().Be(SqlErrorTranslator.SingleStatementMessage);
    }

    [DockerFact]
    public async Task ConsultaInfinita_DevolveTimeoutAmigavel()
    {
        var client = await LoggedInClientAsync();

        var outcome = await ExecuteAsync(client, factory.ExerciseId, "SELECT pg_sleep(60);");

        outcome.Status.Should().Be(ExecutionStatus.Timeout);
        outcome.FriendlyMessage.Should().Be(SqlErrorTranslator.TimeoutMessage);
        outcome.Rows.Should().BeEmpty();
        // A remoção do container pode terminar logo depois de o resultado ser publicado: espera um pouco.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DockerAvailability.SandboxContainerCount() > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(250);
        DockerAvailability.SandboxContainerCount().Should().Be(0, "o container é removido depois do timeout");
    }

    [DockerFact]
    public async Task ConsultaPesadaDeCpu_TambemViraTimeout()
    {
        var client = await LoggedInClientAsync();

        // 10^10 linhas de produto cartesiano: só CPU, sem encher disco/memória (uma recursão infinita
        // estoura o limite de arquivo temporário antes e vira "error", o que também é aceitável).
        var outcome = await ExecuteAsync(client, factory.ExerciseId,
            "SELECT count(*) FROM generate_series(1, 100000) AS a CROSS JOIN generate_series(1, 100000) AS b;");

        outcome.Status.Should().Be(ExecutionStatus.Timeout, outcome.FriendlyMessage + " | " + outcome.TechnicalDetail);
        outcome.FriendlyMessage.Should().Be(SqlErrorTranslator.TimeoutMessage);
    }

    [DockerFact]
    public async Task ColunaErrada_TraduzErroEMostraDetalheTecnico()
    {
        var client = await LoggedInClientAsync();

        var outcome = await ExecuteAsync(client, factory.ExerciseId, "SELECT nme FROM alunos;");

        outcome.Status.Should().Be(ExecutionStatus.Error);
        outcome.FriendlyMessage.Should().Contain("nme");
        outcome.TechnicalDetail.Should().Contain("42703").And.Contain("does not exist");
    }

    [DockerFact]
    public async Task MuitasLinhas_MostraNoMaximo200EMarcaTruncated()
    {
        var client = await LoggedInClientAsync();

        var outcome = await ExecuteAsync(client, factory.ExerciseId, "SELECT g AS numero FROM generate_series(1, 500) AS g;");

        outcome.Columns.Should().Equal("numero");
        outcome.Rows.Should().HaveCount(200);
        outcome.Truncated.Should().BeTrue();
    }

    [DockerFact]
    public async Task LimiteDeTentativas_SegundaExecucaoDevolve200ComAttemptsExhausted()
    {
        var client = await LoggedInClientAsync();

        var first = await ExecuteAsync(client, factory.LimitedExerciseId, "SELECT 1;");
        var second = await client.PostAsJsonAsync(ExecuteUrl(factory.LimitedExerciseId), new { code = "SELECT 1;" });

        first.SubmissionId.Should().NotBeNull();
        first.RemainingAttempts.Should().Be(0);
        second.StatusCode.Should().Be(HttpStatusCode.OK, "429 é só do rate limit");
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("attempts_exhausted");
        body.GetProperty("friendlyMessage").GetString().Should().Be(ExecutionOutcome.AttemptsExhaustedMessage(1));
        body.GetProperty("remainingAttempts").GetInt32().Should().Be(0);
        body.GetProperty("submissionId").ValueKind.Should().Be(JsonValueKind.Null, "nada é gravado depois do limite");
    }

    // ---------- Acesso por progresso (#27, ADR-007) ----------

    [Fact]
    public async Task SemTrilha_OutraTrilhaEBloqueada_Devolvem404IgualAoInexistente()
    {
        var (noPath, _) = await StudentAsync(enroll: false);
        var (enrolled, _) = await StudentAsync();

        var missing = await enrolled.PostAsJsonAsync(ExecuteUrl(Guid.NewGuid()), new { code = "SELECT 1;" });
        var responses = new[]
        {
            await noPath.PostAsJsonAsync(ExecuteUrl(factory.ExerciseId), new { code = "SELECT 1;" }),
            await enrolled.PostAsJsonAsync(ExecuteUrl(factory.OtherPathExerciseId), new { code = "SELECT 1;" }),
            await enrolled.PostAsJsonAsync(ExecuteUrl(factory.WhereExerciseId), new { code = "SELECT 1;" }),
        };

        var missingBody = await missing.Content.ReadAsStringAsync();
        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().Be(missingBody, "não revela se existe, é de outra trilha ou está bloqueado");
        }
    }

    [Fact]
    public async Task AulaLiberadaDepoisDeConcluirAAnterior_DeixaDeSer404()
    {
        // Sem Worker: só o portão. Com a aula 1 concluída, a aula 2 passa do 404 (cai no 503 da fila sem Worker).
        await using var noWorker = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Execution:QueueKey", $"devpath:teste:sem-worker:{Guid.NewGuid():N}");
            b.UseSetting("Execution:ResultTimeoutSeconds", "1");
        });
        var (client, userId) = await StudentAsync(noWorker);
        (await client.PostAsJsonAsync(ExecuteUrl(factory.WhereExerciseId), new { code = "SELECT 1;" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SetProgressAsync(userId, factory.SelectLessonId, LessonCompletionKind.Read, null);

        (await client.PostAsJsonAsync(ExecuteUrl(factory.WhereExerciseId), new { code = "SELECT 1;" }))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Mutação: desliga o portão de Locked. Se este teste falhasse ao "ainda ser 404", a checagem
    /// de desbloqueio não estaria ligada — e SemTrilha_OutraTrilhaEBloqueada passaria por acidente.
    /// </summary>
    [Fact]
    public async Task Mutation_SemChecarDesbloqueio_ExercicioBloqueadoDeixaDeSer404()
    {
        await using var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Execution:QueueKey", $"devpath:teste:mutation-locked:{Guid.NewGuid():N}");
            b.UseSetting("Execution:ResultTimeoutSeconds", "1");
            b.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<ILessonAccessPolicy, AlwaysAllowLessonAccessPolicy>()));
        });
        var (client, _) = await StudentAsync(app);

        var response = await client.PostAsJsonAsync(ExecuteUrl(factory.WhereExerciseId), new { code = "SELECT 1;" });

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "com a política mutante (sempre Allowed), a aula 2 bloqueada deixa de ser 404");
    }

    /// <summary>Política mutante: libera tudo. Só para provar que o Locked do real importa.</summary>
    private sealed class AlwaysAllowLessonAccessPolicy : ILessonAccessPolicy
    {
        public Task<ExerciseAccess> EvaluateAsync(Guid userId, Guid exerciseId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExerciseAccess.Allowed);
    }

    [DockerFact]
    public async Task Pass_ConcluiAAulaNaMesmaTransacao_ERecalculaAMatricula()
    {
        var (client, userId) = await StudentAsync();
        await SetProgressAsync(userId, factory.SelectLessonId, LessonCompletionKind.Read, null);

        var fail = await ExecuteAsync(client, factory.WhereExerciseId, "SELECT nome FROM alunos;");
        fail.Status.Should().Be(ExecutionStatus.Fail);
        fail.LessonCompleted.Should().BeFalse();

        var pass = await ExecuteAsync(client, factory.WhereExerciseId, "SELECT nome, nota FROM alunos ORDER BY nome;");
        pass.Status.Should().Be(ExecutionStatus.Pass, pass.FriendlyMessage + pass.TechnicalDetail);
        pass.LessonCompleted.Should().BeTrue();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        var row = await db.UserProgress.AsNoTracking().SingleAsync(p => p.UserId == userId && p.LessonId == factory.WhereLessonId);
        row.Status.Should().Be(ProgressStatus.Completed);
        row.CompletionKind.Should().Be(LessonCompletionKind.ExercisePassed);
        row.Score.Should().Be(10, "pontos cheios, sem desconto pela tentativa errada");
        var enrollment = await db.UserPathEnrollments.AsNoTracking().SingleAsync(e => e.UserId == userId && e.IsActive);
        enrollment.ProgressPercentage.Should().Be(100m, "as 2 aulas disponíveis da trilha estão concluídas");

        var again = await ExecuteAsync(client, factory.WhereExerciseId, "SELECT nome, nota FROM alunos ORDER BY nome;");
        again.LessonCompleted.Should().BeFalse("já estava concluída com pontos");
    }

    [DockerFact]
    public async Task PassDepoisDoSeguirSemPontos_PromoveParaPontosCheios_MantendoAData()
    {
        var (client, userId) = await StudentAsync();
        await SetProgressAsync(userId, factory.SelectLessonId, LessonCompletionKind.Read, null);
        await SetProgressAsync(userId, factory.WhereLessonId, LessonCompletionKind.SkippedAfterFailures, 0);
        DateTime? skippedAt;
        using (var scope = factory.Services.CreateScope())
        {
            skippedAt = (await scope.ServiceProvider.GetRequiredService<DevPathDbContext>().UserProgress.AsNoTracking()
                .SingleAsync(p => p.UserId == userId && p.LessonId == factory.WhereLessonId)).CompletedAt;
        }

        var pass = await ExecuteAsync(client, factory.WhereExerciseId, "SELECT nome, nota FROM alunos ORDER BY nome;");

        pass.LessonCompleted.Should().BeTrue("promoveu para ExercisePassed");
        using (var scope = factory.Services.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<DevPathDbContext>().UserProgress.AsNoTracking()
                .SingleAsync(p => p.UserId == userId && p.LessonId == factory.WhereLessonId);
            row.CompletionKind.Should().Be(LessonCompletionKind.ExercisePassed);
            row.Score.Should().Be(10);
            row.CompletedAt.Should().BeCloseTo(skippedAt!.Value, TimeSpan.FromMilliseconds(1), "a data da conclusão é a original");
        }
    }

    // ---------- Ajudantes ----------

    private async Task<ExecutionOutcome> ExecuteAsync(HttpClient client, Guid exerciseId, string code)
    {
        factory.WorkerRunning.Should().BeTrue("o Worker sobe quando há Docker");
        var response = await client.PostAsJsonAsync(ExecuteUrl(exerciseId), new { code });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ExecutionOutcome>(Json))!;
    }

    private Task<HttpClient> LoggedInClientAsync(bool withAntiforgery = true) => LoggedInClientAsync(factory, withAntiforgery);

    private async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> app, bool withAntiforgery = true) =>
        (await StudentAsync(app, withAntiforgery: withAntiforgery)).Client;

    /// <summary>
    /// Cadastra um aluno novo (já fica logado), matricula na trilha da factory (#27: só executa quem está na trilha)
    /// e, se pedido, coloca o header antiforgery no cliente.
    /// </summary>
    private async Task<(HttpClient Client, Guid UserId)> StudentAsync(
        WebApplicationFactory<Program>? app = null, bool enroll = true, bool withAntiforgery = true)
    {
        app ??= factory;
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var email = $"{Guid.NewGuid():N}@devpath.test";
        var register = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Aluna Teste",
            ["Email"] = email,
            ["Password"] = "Senha123",
            ["ConfirmPassword"] = "Senha123"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Redirect, "o cadastro deveria logar o aluno");

        Guid userId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
            userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            if (enroll)
            {
                var journey = scope.ServiceProvider.GetRequiredService<IStudentJourneyService>();
                (await journey.EnrollAsync(userId, factory.PathSlug)).Kind.Should().Be(EnrollmentResultKind.Enrolled);
            }
        }

        if (withAntiforgery)
        {
            // O que o Frontend faz: busca o token depois do login e manda no header em todo POST.
            var token = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery/token");
            client.DefaultRequestHeaders.Add(token.GetProperty("headerName").GetString()!, token.GetProperty("token").GetString());
        }
        return (client, userId);
    }

    private async Task SetProgressAsync(Guid userId, Guid lessonId, LessonCompletionKind kind, int? score)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        var progress = UserProgress.StartLesson(userId, lessonId);
        progress.Complete(score, kind);
        db.UserProgress.Add(progress);
        await db.SaveChangesAsync();
    }

    private static async Task<string?> MessageAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString();

    private static async Task<(string? Message, string? Code)> ErrorAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var message = json.GetProperty("message").GetString();
        var code = json.TryGetProperty("code", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetString() : null;
        return (message, code);
    }
}
