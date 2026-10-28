using System.Net;
using System.Text.RegularExpressions;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.Web.Onboarding;
using DevPath.Web.ViewModels.Onboarding;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevPath.IntegrationTests.Onboarding;

/// <summary>
/// Fluxo da issue #11 (US-11.1 a US-11.4): home → cadastro → "Escolha sua trilha" → dashboard.
/// Os testes de tela trocam a porta por <see cref="FakeStudentJourneyGateway"/> (dados de exemplo em cada estado);
/// um teste usa o serviço real com o banco sem trilhas (estado vazio, nunca 500). O fluxo com o serviço real e
/// trilhas no banco está em <see cref="OnboardingRealServiceTests"/>.
/// </summary>
[Collection(WebAppCollection.Name)]
public sealed class OnboardingFlowTests(DevPathWebApplicationFactory factory)
{
    private const string ValidPassword = "Senha123";
    private const string ChoosePathUrl = "/escolha-sua-trilha";

    [Fact]
    public async Task Home_Visitante_TemUmUnicoBotaoPrincipalComecarAgoraParaOCadastro()
    {
        var client = factory.CreateBrowserClient();

        var html = await (await client.GetAsync("/")).ReadHtmlAsync();

        Regex.Matches(html, @"class=""btn-primary\b").Count.Should().Be(1, "a home tem uma única ação principal");
        html.Should().MatchRegex(@"<a (?=[^>]*href=""/Account/Register"")(?=[^>]*class=""btn-primary)[^>]*>Começar Agora</a>");
        html.Should().Contain("Já tenho conta").And.Contain("href=\"/Account/Login\"");
    }

    [Fact]
    public async Task Home_NaoTemMaisOExemploDeHtmx()
    {
        var client = factory.CreateBrowserClient();

        var html = await (await client.GetAsync("/")).ReadHtmlAsync();

        html.Should().NotContain("hx-get").And.NotContain("server-time").And.NotContain("Exemplo de HTMX");
        (await client.GetAsync("/Home/ServerTime")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    [InlineData("/Account/AccessDenied")]
    public async Task Paginas_TemUmUnicoBotaoPrincipal_ECriarContaDoCabecalhoEhContornado(string url)
    {
        var html = await (await factory.CreateBrowserClient().GetAsync(url)).ReadHtmlAsync();

        Regex.Matches(html, @"class=""btn-primary\b").Count.Should().Be(1, "cada tela tem uma única ação principal");
        var headerLinks = Regex.Matches(html, @"<a (?=[^>]*href=""/Account/Register"")(?![^>]*btn-primary)(?=[^>]*class=""[^""]*\bborder border-brand-600\b)[^>]*>Criar conta</a>");
        headerLinks.Count.Should().Be(2, "o \"Criar conta\" do cabeçalho (desktop e menu mobile) é contornado, não primário");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    [InlineData("/Account/AccessDenied")]
    public async Task Paginas_TemTemaEscuroEBotaoDeTema(string url)
    {
        var html = await (await factory.CreateBrowserClient().GetAsync(url)).ReadHtmlAsync();

        html.Should().Contain("localStorage.getItem(\"devpath-theme\")", "o tema é aplicado antes do CSS");
        html.Should().Contain("data-theme-toggle").And.Contain("aria-pressed=\"false\"");
        html.Should().Contain("dark:bg-slate-950");
    }

    [Fact]
    public async Task EscolhaDeTrilha_SemLogin_RedirecionaParaLoginComReturnUrl()
    {
        var response = await factory.CreateBrowserClient().GetAsync(ChoosePathUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Account/Login?ReturnUrl=%2Fescolha-sua-trilha");
    }

    [Fact]
    public async Task Cadastro_VaiDiretoParaEscolhaDeTrilha()
    {
        var client = factory.CreateBrowserClient();

        var response = await RegisterAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(ChoosePathUrl);
    }

    [Fact]
    public async Task ServicoReal_BancoSemTrilhas_TelasMostramEstadoVazioSemErro()
    {
        var client = factory.CreateBrowserClient();
        await RegisterAsync(client);

        var dashboard = await client.GetAsync("/dashboard");
        dashboard.StatusCode.Should().Be(HttpStatusCode.Redirect);
        dashboard.Headers.Location!.ToString().Should().Be(ChoosePathUrl);

        var choose = await client.GetAsync(ChoosePathUrl);
        choose.StatusCode.Should().Be(HttpStatusCode.OK);
        (await choose.ReadHtmlAsync()).Should().Contain("Estamos preparando as trilhas. Volte em breve.");

        var post = await client.SubmitFormAsync(ChoosePathUrl, ChoosePathUrl,
            new Dictionary<string, string> { ["pathSlug"] = "sql-dados" });
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Be(ChoosePathUrl);
        (await (await client.GetAsync(ChoosePathUrl)).ReadHtmlAsync())
            .Should().Contain("Não encontramos essa trilha. Escolha uma das opções abaixo.");
    }

    [Fact]
    public async Task EscolhaDeTrilha_ListaTrilhasComNivelHorasEAulas()
    {
        var client = CreateClientWith(new FakeStudentJourneyGateway());
        await RegisterAsync(client);

        var html = await (await client.GetAsync(ChoosePathUrl)).ReadHtmlAsync();

        html.Should().Contain("Escolha sua trilha")
            .And.Contain("SQL & Dados")
            .And.Contain("Para iniciantes")
            .And.Contain("Cerca de 40 h")
            .And.Contain("5 aulas disponíveis")
            .And.Contain("Aulas em breve")
            .And.Contain("name=\"pathSlug\" value=\"sql-dados\"");
        Regex.Matches(html, @"class=""btn-primary\b").Count.Should().BeLessThanOrEqualTo(1,
            "os cards de trilha são opções equivalentes (btn-secondary); nunca mais de um primário");
    }

    [Fact]
    public async Task FluxoCompleto_CadastroEscolhaDashboard_ComConfirmacaoEComecarPelaAula1()
    {
        var client = CreateClientWith(new FakeStudentJourneyGateway());
        await RegisterAsync(client, "Bruno Aluno");

        var post = await ChooseSqlAsync(client);
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Be("/Dashboard");

        var dashboard = await client.GetAsync("/dashboard");
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await dashboard.ReadHtmlAsync();
        html.Should().Contain("Você começou a trilha SQL & Dados!")
            .And.Contain("Olá, Bruno Aluno!")
            .And.Contain("0% concluído — 0 de 5 aulas")
            .And.Contain("aria-valuenow=\"0\"")
            .And.MatchRegex(@"href=""/trilhas/sql-dados/primeiros-passos-com-sql/seu-primeiro-select""[^>]*data-continue>\s*Começar pela aula 1");
        Regex.Matches(html, @"class=""btn-primary\b").Count.Should().Be(1, "o dashboard tem uma única ação principal");

        // A confirmação aparece uma vez só (TempData).
        (await (await client.GetAsync("/dashboard")).ReadHtmlAsync()).Should().NotContain("Você começou a trilha");
    }

    [Fact]
    public async Task JaEscolheuTrilha_EscolhaRedirecionaParaODashboard()
    {
        var client = CreateClientWith(new FakeStudentJourneyGateway());
        await RegisterAsync(client);
        await ChooseSqlAsync(client);

        var response = await client.GetAsync(ChoosePathUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Dashboard");
    }

    [Fact]
    public async Task EscolhaDeTrilha_SlugInexistente_VoltaComMensagemAmigavel()
    {
        var client = CreateClientWith(new FakeStudentJourneyGateway());
        await RegisterAsync(client);

        var post = await client.SubmitFormAsync(ChoosePathUrl, ChoosePathUrl,
            new Dictionary<string, string> { ["pathSlug"] = "nao-existe" });

        post.Headers.Location!.ToString().Should().Be(ChoosePathUrl);
        (await (await client.GetAsync(ChoosePathUrl)).ReadHtmlAsync())
            .Should().Contain("Não encontramos essa trilha. Escolha uma das opções abaixo.");
    }

    [Fact]
    public async Task Dashboard_EmAndamento_MostraRoadmapComStatusEmTextoEMotivoDoBloqueio()
    {
        var client = CreateClientWith(new FakeStudentJourneyGateway { Dashboard = _ => FakeStudentJourneyGateway.Samples.InProgress() });
        await RegisterAsync(client);
        await ChooseSqlAsync(client);

        var html = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();

        html.Should().Contain("60% concluído — 3 de 5 aulas")
            .And.MatchRegex(@"href=""/trilhas/sql-dados/primeiros-passos-com-sql/juntando-tabelas-com-join""[^>]*data-continue>\s*Continuar")
            .And.Contain("Concluída").And.Contain("Em andamento").And.Contain("Bloqueada").And.Contain("Em breve")
            .And.Contain("Conclua '<a href=\"/trilhas/sql-dados/primeiros-passos-com-sql/juntando-tabelas-com-join\" class=\"link\">Juntando tabelas com JOIN</a>' para liberar esta aula")
            .And.Contain("Esta aula está sendo escrita");
        // Aula bloqueada e "Em breve" não viram link.
        html.Should().NotContain("href=\"/trilhas/sql-dados/primeiros-passos-com-sql/resumindo-dados-com-group-by\"")
            .And.NotContain("href=\"/trilhas/sql-dados/ambiente/o-que-e-banco-relacional\"");
    }

    [Fact]
    public async Task Dashboard_Roadmap_SegueAOrdemDo22_PrimeirosPassosComSqlEhOModulo1()
    {
        var client = CreateClientWith(new FakeStudentJourneyGateway());
        await RegisterAsync(client);
        await ChooseSqlAsync(client);

        var html = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();

        html.Should().MatchRegex(@"Módulo 1 ·</span>\s*Primeiros passos com SQL")
            .And.MatchRegex(@"Módulo 2 ·</span>\s*Ambiente: banco relacional \+ Docker");
        // Aulas do módulo 1 na ordem do #22, com JOIN antes de GROUP BY (decisão do dono).
        var titles = new[]
        {
            "Seu primeiro SELECT", "Filtrando com WHERE", "Ordenando e limitando com ORDER BY e LIMIT",
            "Juntando tabelas com JOIN", "Resumindo dados com COUNT, SUM, AVG e GROUP BY"
        };
        var positions = titles.Select(t => html.IndexOf(t, StringComparison.Ordinal)).ToArray();
        positions.Should().OnlyContain(p => p >= 0).And.BeInAscendingOrder();
    }

    [Fact]
    public async Task Dashboard_MotivoDoBloqueio_FicaSempreVisivel_SemDependerDeHoverOuFoco()
    {
        var client = CreateClientWith(new FakeStudentJourneyGateway());
        await RegisterAsync(client);
        await ChooseSqlAsync(client);

        var html = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();

        var lockTexts = Regex.Matches(html, @"<p class=""(?<cls>[^""]*)"">\s*Conclua '");
        lockTexts.Count.Should().Be(4, "as aulas 2 a 5 do módulo 1 estão bloqueadas no primeiro acesso");
        foreach (Match m in lockTexts)
        {
            m.Groups["cls"].Value.Should().NotMatchRegex(@"(^|\s)(hidden|invisible|sr-only|opacity-0)(\s|$)")
                .And.NotMatchRegex(@"(group-|peer-)?(hover|focus|focus-within|focus-visible):");
        }
    }

    [Fact]
    public async Task Dashboard_TudoConcluido_MostraMensagemSemBotaoContinuar()
    {
        var client = CreateClientWith(new FakeStudentJourneyGateway { Dashboard = _ => FakeStudentJourneyGateway.Samples.AllDone() });
        await RegisterAsync(client);
        await ChooseSqlAsync(client);

        var html = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();

        html.Should().Contain("Você concluiu tudo que está disponível").And.Contain("Novas aulas em breve.")
            .And.NotContain("data-continue");
    }

    [Fact]
    public async Task Dashboard_TrilhaSemAulas_MostraEstadoVazioSemZeroDeZero()
    {
        var client = CreateClientWith(new FakeStudentJourneyGateway { Dashboard = _ => FakeStudentJourneyGateway.Samples.Empty() });
        await RegisterAsync(client);
        await ChooseSqlAsync(client);

        var response = await client.GetAsync("/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.ReadHtmlAsync();

        html.Should().Contain("Esta trilha ainda está sendo preparada.")
            .And.NotContain("0 de 0").And.NotContain("data-continue").And.NotContain("role=\"progressbar\"");
    }

    [Fact]
    public async Task EscolhaDeTrilha_SemTokenAntiforgery_EhRejeitada()
    {
        var client = CreateClientWith(new FakeStudentJourneyGateway());
        await RegisterAsync(client);

        var response = await client.PostAsync(ChoosePathUrl,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["pathSlug"] = "sql-dados" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private HttpClient CreateClientWith(IStudentJourneyGateway gateway) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStudentJourneyGateway>();
                services.AddSingleton(gateway);
            }))
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
                // "Testing" se comporta como produção: cookie Secure + redirect para HTTPS.
                BaseAddress = new Uri("https://localhost")
            });

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string name = "Ana Aluna") =>
        client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = name,
            ["Email"] = $"aluno-{Guid.NewGuid():N}@devpath.test",
            ["Password"] = ValidPassword,
            ["ConfirmPassword"] = ValidPassword
        });

    private static Task<HttpResponseMessage> ChooseSqlAsync(HttpClient client) =>
        client.SubmitFormAsync(ChoosePathUrl, ChoosePathUrl,
            new Dictionary<string, string> { ["pathSlug"] = FakeStudentJourneyGateway.SqlPath });
}
