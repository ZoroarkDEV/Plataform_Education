using System.Net;
using System.Text.RegularExpressions;
using DevPath.Infrastructure.Identity;
using DevPath.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.IntegrationTests.Onboarding;

/// <summary>
/// Fluxo do #11 ponta a ponta com o serviço real do Backend (sem dublê): adapter + IStudentJourneyService + Postgres.
/// </summary>
[Collection(JourneyAppCollection.Name)]
public sealed class OnboardingRealServiceTests(JourneyWebApplicationFactory factory)
{
    private const string ValidPassword = "Senha123";
    private const string ChoosePathUrl = "/escolha-sua-trilha";

    [Fact]
    public async Task Escolha_ListaAsTrilhasAtivasDoBanco_ComAulasDisponiveisSemRascunho()
    {
        var client = factory.CreateBrowserClient();
        await RegisterAsync(client);

        var response = await client.GetAsync(ChoosePathUrl);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.ReadHtmlAsync();

        html.Should().Contain("SQL Teste").And.Contain("Backend Teste").And.Contain("So Rascunhos").And.Contain("Sem Aulas")
            .And.Contain("2 aulas disponíveis", "o rascunho não conta como aula disponível")
            .And.Contain("1 aula disponível")
            .And.Contain($"name=\"pathSlug\" value=\"{factory.SqlSlug}\"");
        html.IndexOf("SQL Teste", StringComparison.Ordinal).Should()
            .BeLessThan(html.IndexOf("Backend Teste", StringComparison.Ordinal), "a ordem vem do serviço");
    }

    [Fact]
    public async Task FluxoCompleto_MatriculaEDashboardComRoadmapEMotivoDoBloqueio()
    {
        var client = factory.CreateBrowserClient();
        await RegisterAsync(client);

        var post = await ChooseAsync(client, factory.SqlSlug);
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Be("/Dashboard");

        var response = await client.GetAsync("/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.ReadHtmlAsync();

        var module = "primeiros-passos";
        html.Should().Contain("Você começou a trilha SQL Teste!")
            .And.Contain("0% concluído — 0 de 2 aulas", "o % e o total usam AvailableLessons, sem o rascunho")
            .And.MatchRegex($@"href=""/trilhas/{factory.SqlSlug}/{module}/primeira-aula""[^>]*data-continue>\s*Começar pela aula 1")
            .And.Contain($"Conclua '<a href=\"/trilhas/{factory.SqlSlug}/{module}/primeira-aula\" class=\"link\">Primeira Aula</a>' para liberar esta aula")
            .And.Contain("Aula Rascunho").And.Contain("Esta aula está sendo escrita");
        html.Should().NotContain($"href=\"/trilhas/{factory.SqlSlug}/{module}/segunda-aula\"", "aula bloqueada não vira link")
            .And.NotContain("0 de 3");
        Regex.Matches(html, @"class=""btn-primary\b").Count.Should().Be(1);

        // Já matriculado: a escolha manda direto para o dashboard.
        var again = await client.GetAsync(ChoosePathUrl);
        again.StatusCode.Should().Be(HttpStatusCode.Redirect);
        again.Headers.Location!.ToString().Should().Be("/Dashboard");
    }

    [Fact]
    public async Task SegundaMatricula_EmOutraTrilha_NaoTrocaEMostraAviso()
    {
        var client = factory.CreateBrowserClient();
        await RegisterAsync(client);
        await ChooseAsync(client, factory.SqlSlug);
        // Simula a escolha aberta em outra aba: a página de escolha já redireciona, então o token
        // antiforgery vem do dashboard (o que também consome a confirmação da 1ª matrícula).
        var post = await client.SubmitFormAsync("/dashboard", ChoosePathUrl,
            new Dictionary<string, string> { ["pathSlug"] = factory.BackendSlug });

        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Be("/Dashboard");
        var html = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();
        html.Should().Contain("Você já está na trilha SQL Teste. Por enquanto é uma trilha por vez")
            .And.Contain("SQL Teste").And.NotContain("Backend Teste");
    }

    [Fact]
    public async Task TrilhaSemNenhumaAula_VoltaParaAEscolhaComMensagem()
    {
        var client = factory.CreateBrowserClient();
        await RegisterAsync(client);

        var post = await ChooseAsync(client, factory.NoLessonsSlug);

        post.Headers.Location!.ToString().Should().Be(ChoosePathUrl);
        var html = await (await client.GetAsync(ChoosePathUrl)).ReadHtmlAsync();
        html.Should().Contain("Essa trilha ainda não tem aulas.")
            .And.NotContain("Não conseguimos salvar sua escolha agora.");
        (await client.GetAsync("/dashboard")).Headers.Location!.ToString().Should().Be(ChoosePathUrl, "não matriculou");
    }

    [Fact]
    public async Task TrilhaSoComRascunhos_NaoAceitaAMatricula_VoltaParaAEscolhaComMensagem()
    {
        var client = factory.CreateBrowserClient();
        await RegisterAsync(client);

        // Contrato novo: trilha só com rascunhos devolve NoLessons, como a trilha sem nenhuma aula.
        var post = await ChooseAsync(client, factory.DraftsOnlySlug);
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Be(ChoosePathUrl);
        var html = await (await client.GetAsync(ChoosePathUrl)).ReadHtmlAsync();
        html.Should().Contain("Essa trilha ainda não tem aulas.");
        (await client.GetAsync("/dashboard")).Headers.Location!.ToString().Should().Be(ChoosePathUrl, "não matriculou");
    }

    [Fact]
    public async Task ContaApagadaComCookieValido_SemErro500_EncerraASessaoNoLogin()
    {
        var client = factory.CreateBrowserClient();
        var email = await RegisterAsync(client);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.DeleteAsync((await users.FindByEmailAsync(email))!)).Succeeded.Should().BeTrue();
        }

        // GetDashboardAsync devolve null: vai para a escolha, sem 500.
        var dashboard = await client.GetAsync("/dashboard");
        dashboard.StatusCode.Should().Be(HttpStatusCode.Redirect);
        dashboard.Headers.Location!.ToString().Should().Be(ChoosePathUrl);
        (await client.GetAsync(ChoosePathUrl)).StatusCode.Should().Be(HttpStatusCode.OK);

        // EnrollAsync devolve UserNotFound (sem exceção): a Web encerra a sessão e manda para o login.
        var post = await ChooseAsync(client, factory.SqlSlug);
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Be("/Account/Login");

        var after = await client.GetAsync(ChoosePathUrl);
        after.StatusCode.Should().Be(HttpStatusCode.Redirect);
        after.Headers.Location!.ToString().Should().Contain("/Account/Login?ReturnUrl=");
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var email = $"aluno-{Guid.NewGuid():N}@devpath.test";
        var response = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Ana Aluna",
            ["Email"] = email,
            ["Password"] = ValidPassword,
            ["ConfirmPassword"] = ValidPassword
        });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return email;
    }

    private static Task<HttpResponseMessage> ChooseAsync(HttpClient client, string slug) =>
        client.SubmitFormAsync(ChoosePathUrl, ChoosePathUrl, new Dictionary<string, string> { ["pathSlug"] = slug });
}
