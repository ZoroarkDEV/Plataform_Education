using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevPath.Application.CodeExecution;
using DevPath.Infrastructure.CodeExecution;
using DevPath.Infrastructure.Persistence;
using DevPath.IntegrationTests.CodeExecution;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.Web.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        (await MessageAsync(response)).Should().Be(ExerciseExecutionEndpoints.AntiforgeryMessage);
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
        (await MessageAsync(response)).Should().Be(ExerciseExecutionEndpoints.EmptyCodeMessage);
    }

    [Fact]
    public async Task AulaEmRascunho_Devolve403()
    {
        var client = await LoggedInClientAsync();

        var response = await client.PostAsJsonAsync(ExecuteUrl(factory.DraftExerciseId), new { code = "SELECT 1;" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await MessageAsync(response)).Should().Be(ExerciseExecutionEndpoints.DraftMessage);
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
    public async Task RespostaJson_UsaOContratoDoFrontend()
    {
        var client = await LoggedInClientAsync();

        var response = await client.PostAsJsonAsync(ExecuteUrl(factory.ExerciseId), new { code = "SELECT nome FROM alunos ORDER BY nome;" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("fail");
        foreach (var field in new[] { "columns", "rows", "truncated", "friendlyMessage", "technicalDetail", "executionTimeMs",
                     "passedCases", "totalCases", "testCases", "submissionId", "remainingAttempts" })
        {
            body.TryGetProperty(field, out _).Should().BeTrue($"o campo {field} faz parte do contrato");
        }
        var testCase = body.GetProperty("testCases")[0];
        testCase.GetProperty("name").GetString().Should().Be("notas por nome");
        testCase.GetProperty("passed").GetBoolean().Should().BeFalse();
        testCase.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();

        // O retorno nunca entrega a resposta: nem as linhas esperadas (as notas) nem o formato do caso de teste.
        var raw = body.GetRawText();
        raw.Should().NotContain("\"9\"").And.NotContain("orderMatters").And.NotContain("expected").And.NotContain("solution");
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
    public async Task LimiteDeTentativas_SegundaExecucaoDevolveErroAmigavel()
    {
        var client = await LoggedInClientAsync();

        var first = await ExecuteAsync(client, factory.LimitedExerciseId, "SELECT 1;");
        var second = await ExecuteAsync(client, factory.LimitedExerciseId, "SELECT 1;");

        first.SubmissionId.Should().NotBeNull();
        first.RemainingAttempts.Should().Be(0);
        second.Status.Should().Be(ExecutionStatus.Error);
        second.FriendlyMessage.Should().Be("Você já usou a única tentativa deste exercício.");
        second.RemainingAttempts.Should().Be(0);
        second.SubmissionId.Should().BeNull("nada é gravado depois do limite");
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

    /// <summary>Cadastra um aluno novo (já fica logado) e, se pedido, coloca o header antiforgery no cliente.</summary>
    private static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> app, bool withAntiforgery = true)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        var register = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Aluna Teste",
            ["Email"] = $"{Guid.NewGuid():N}@devpath.test",
            ["Password"] = "Senha123",
            ["ConfirmPassword"] = "Senha123"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Redirect, "o cadastro deveria logar o aluno");

        if (withAntiforgery)
        {
            // O que o Frontend faz: busca o token depois do login e manda no header em todo POST.
            var token = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery/token");
            client.DefaultRequestHeaders.Add(token.GetProperty("headerName").GetString()!, token.GetProperty("token").GetString());
        }
        return client;
    }

    private static async Task<string?> MessageAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString();
}
