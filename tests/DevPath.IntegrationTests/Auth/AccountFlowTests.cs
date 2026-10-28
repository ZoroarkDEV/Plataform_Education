using System.Net;
using DevPath.Infrastructure.Identity;
using DevPath.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.IntegrationTests.Auth;

/// <summary>Fluxos de cadastro, login, logout e rota protegida, de ponta a ponta (HTTP → banco).</summary>
[Collection(WebAppCollection.Name)]
public sealed class AccountFlowTests(DevPathWebApplicationFactory factory)
{
    private const string ValidPassword = "Senha123";

    [Fact]
    public async Task Dashboard_SemLogin_RedirecionaParaLoginComReturnUrl()
    {
        var client = factory.CreateBrowserClient();

        var response = await client.GetAsync("/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Account/Login?ReturnUrl=%2Fdashboard");
    }

    [Fact]
    public async Task Register_ComDadosValidos_CriaUsuarioEJaAutentica()
    {
        var client = factory.CreateBrowserClient();
        var email = NewEmail();

        var response = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Ana Aluna",
            ["Email"] = email,
            ["Password"] = ValidPassword,
            ["ConfirmPassword"] = ValidPassword
        });

        // US-11.1: depois do cadastro, vai direto para "Escolha sua trilha", já autenticado.
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/escolha-sua-trilha");

        var choosePath = await client.GetAsync("/escolha-sua-trilha");
        choosePath.StatusCode.Should().Be(HttpStatusCode.OK);
        (await choosePath.ReadHtmlAsync()).Should().Contain("Escolha sua trilha").And.Contain("Ana Aluna");

        var user = await FindUserAsync(email);
        user.Should().NotBeNull();
        user!.DisplayName.Should().Be("Ana Aluna");
        user.PreferredLanguage.Should().Be("pt-BR");
        user.PasswordHash.Should().NotBe(ValidPassword, "a senha é gravada só como hash");
    }

    [Fact]
    public async Task Register_ComSenhasDiferentes_MostraErroDeValidacao()
    {
        var client = factory.CreateBrowserClient();

        var response = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Ana",
            ["Email"] = NewEmail(),
            ["Password"] = ValidPassword,
            ["ConfirmPassword"] = "Outra123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadHtmlAsync()).Should().Contain("As duas senhas não são iguais.");
    }

    [Fact]
    public async Task Register_ComEmailJaUsado_MostraErroEmPortugues()
    {
        var email = await CreateUserAsync();
        var client = factory.CreateBrowserClient();

        var response = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Outra Pessoa",
            ["Email"] = email,
            ["Password"] = ValidPassword,
            ["ConfirmPassword"] = ValidPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadHtmlAsync()).Should().Contain("Já existe uma conta com o e-mail");
    }

    [Fact]
    public async Task Login_Valido_RedirecionaParaReturnUrlEAtualizaLastLoginAt()
    {
        var email = await CreateUserAsync();
        var client = factory.CreateBrowserClient();

        var response = await LoginAsync(client, email, ValidPassword, returnUrl: "/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/dashboard");
        // Logado: o dashboard não manda para o login (sem trilha ainda, manda para a escolha de trilha).
        var dashboard = await client.GetAsync("/dashboard");
        dashboard.Headers.Location!.ToString().Should().Be("/escolha-sua-trilha");

        var user = await FindUserAsync(email);
        user!.LastLoginAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Login_ComReturnUrlExterna_IgnoraEVaiParaODashboard()
    {
        var email = await CreateUserAsync();
        var client = factory.CreateBrowserClient();

        var response = await LoginAsync(client, email, ValidPassword, returnUrl: "https://site-malicioso.example/");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Dashboard");
    }

    [Fact]
    public async Task Login_ComSenhaErrada_MostraErroENaoAutentica()
    {
        var email = await CreateUserAsync();
        var client = factory.CreateBrowserClient();

        var response = await LoginAsync(client, email, "SenhaErrada1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadHtmlAsync()).Should().Contain("E-mail ou senha inválidos.");
        (await client.GetAsync("/dashboard")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Login_Apos5SenhasErradas_BloqueiaAteASenhaCorreta()
    {
        var email = await CreateUserAsync();
        var client = factory.CreateBrowserClient();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await LoginAsync(client, email, "SenhaErrada1");
        }

        var response = await LoginAsync(client, email, ValidPassword);

        // Bloqueada: nem a senha certa entra, e a mensagem é a genérica (não revela o bloqueio).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.ReadHtmlAsync();
        html.Should().Contain(GenericLoginError).And.NotContain("bloqueada");
        (await client.GetAsync("/dashboard")).StatusCode.Should().Be(HttpStatusCode.Redirect);

        var user = await FindUserAsync(email);
        user!.LockoutEnd.Should().BeAfter(DateTimeOffset.UtcNow, "as tentativas erradas continuam contando para o bloqueio");
    }

    [Fact]
    public async Task Login_EmailInexistenteSenhaErradaEContaBloqueada_MostramAMesmaMensagem()
    {
        var client = factory.CreateBrowserClient();
        var existing = await CreateUserAsync();
        var locked = await CreateUserAsync();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await LoginAsync(client, locked, "SenhaErrada1");
        }

        var nonexistent = await ErrorSummaryAsync(await LoginAsync(client, NewEmail(), ValidPassword));
        var wrongPassword = await ErrorSummaryAsync(await LoginAsync(client, existing, "SenhaErrada1"));
        var lockedOut = await ErrorSummaryAsync(await LoginAsync(client, locked, ValidPassword));

        nonexistent.Should().Be(GenericLoginError);
        wrongPassword.Should().Be(nonexistent);
        lockedOut.Should().Be(nonexistent);
    }

    /// <summary>
    /// Teste de tempo propositalmente FROUXO: só pega a diferença grosseira de antes da correção
    /// (sem o hash fictício, e-mail inexistente leva ~3 ms contra ~55 ms de uma conta real, razão ~0,05).
    /// Exigimos só que a mediana do caminho "não existe" seja pelo menos 25% da mediana de "senha errada".
    /// </summary>
    /// <remarks>
    /// Para reduzir o ruído (a versão anterior falhava às vezes):
    /// <list type="bullet">
    /// <item>as contas são criadas ANTES de medir (antes, o cadastro, que também calcula hash, entrava no tempo);</item>
    /// <item>aquecimento dos dois caminhos e 7 rodadas intercaladas (uma de cada por vez), para que uma pausa
    /// da máquina (GC, outro teste em paralelo) atinja os dois lados igualmente;</item>
    /// <item>comparação pela mediana, que ignora picos isolados.</item>
    /// </list>
    /// Verificado localmente: sem o hash fictício do AuthService, este teste falha.
    /// </remarks>
    [Fact]
    public async Task Login_EmailInexistente_LevaTempoParecidoComSenhaErrada()
    {
        const int Rounds = 7;
        var client = factory.CreateBrowserClient();

        // Senha errada em conta real: uma conta por rodada para não chegar no bloqueio.
        var accounts = new List<string>();
        for (var i = 0; i < Rounds + 1; i++)
        {
            accounts.Add(await CreateUserAsync());
        }

        static async Task<double> TimeAsync(Func<Task> action)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await action();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        // Aquecimento (JIT, conexão com o banco, hash fictício calculado na 1ª vez).
        await LoginAsync(client, accounts[Rounds], "SenhaErrada1");
        await LoginAsync(client, NewEmail(), "SenhaErrada1");

        var wrongPassword = new List<double>();
        var nonexistent = new List<double>();
        for (var i = 0; i < Rounds; i++)
        {
            wrongPassword.Add(await TimeAsync(() => LoginAsync(client, accounts[i], "SenhaErrada1")));
            nonexistent.Add(await TimeAsync(() => LoginAsync(client, NewEmail(), "SenhaErrada1")));
        }

        static double Median(List<double> samples) => samples.Order().ElementAt(samples.Count / 2);
        var wrongPasswordMs = Median(wrongPassword);
        var nonexistentMs = Median(nonexistent);

        nonexistentMs.Should().BeGreaterThan(wrongPasswordMs * 0.25,
            $"e-mail inexistente levou {nonexistentMs:F1} ms (mediana) e senha errada {wrongPasswordMs:F1} ms; " +
            $"amostras: [{string.Join(", ", nonexistent.Select(x => x.ToString("F0")))}] x " +
            $"[{string.Join(", ", wrongPassword.Select(x => x.ToString("F0")))}]");
    }

    [Fact]
    public async Task Register_ComEmailJaUsado_MostraOErroUmaVezSo()
    {
        var email = await CreateUserAsync();
        var client = factory.CreateBrowserClient();

        var response = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Outra Pessoa",
            ["Email"] = email,
            ["Password"] = ValidPassword,
            ["ConfirmPassword"] = ValidPassword
        });

        var html = await response.ReadHtmlAsync();
        System.Text.RegularExpressions.Regex.Matches(html, "Já existe uma conta com o e-mail").Should().HaveCount(1);
    }

    [Fact]
    public async Task CookieDeLogin_ForaDoDevelopment_EhSecureEHttpOnly()
    {
        var email = await CreateUserAsync();
        var client = factory.CreateBrowserClient();

        var response = await LoginAsync(client, email, ValidPassword);

        var authCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".DevPath.Auth="));
        authCookie.Should().ContainEquivalentOf("secure").And.ContainEquivalentOf("httponly").And.ContainEquivalentOf("samesite=lax");
    }

    [Fact]
    public async Task Logout_EncerraASessao()
    {
        var email = await CreateUserAsync();
        var client = factory.CreateBrowserClient();
        await LoginAsync(client, email, ValidPassword);

        // O dashboard tem o form de logout? Não: pegamos o token na própria página de login.
        var response = await client.SubmitFormAsync("/Account/Login", "/Account/Logout", new Dictionary<string, string>());

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await client.GetAsync("/dashboard")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Post_SemTokenAntiforgery_EhRejeitado()
    {
        var client = factory.CreateBrowserClient();

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "alguem@devpath.test",
            ["Password"] = ValidPassword
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private const string GenericLoginError = "E-mail ou senha inválidos.";

    // Texto do primeiro erro do resumo de validação (asp-validation-summary) da página de login.
    private static async Task<string> ErrorSummaryAsync(HttpResponseMessage response)
    {
        var html = await response.ReadHtmlAsync();
        var match = System.Text.RegularExpressions.Regex.Match(html, @"validation-summary-errors[^>]*>\s*<ul>\s*<li>(.*?)</li>", System.Text.RegularExpressions.RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string NewEmail() => $"aluno-{Guid.NewGuid():N}@devpath.test";

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, string? returnUrl = null)
    {
        var page = returnUrl is null ? "/Account/Login" : $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        var fields = new Dictionary<string, string> { ["Email"] = email, ["Password"] = password };
        if (returnUrl is not null)
        {
            fields["ReturnUrl"] = returnUrl;
        }

        return client.SubmitFormAsync(page, "/Account/Login", fields);
    }

    // Cria o usuário direto pelo UserManager (sem passar pela tela), para testar só o login.
    private async Task<string> CreateUserAsync()
    {
        var email = NewEmail();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await userManager.CreateAsync(
            new ApplicationUser { UserName = email, Email = email, DisplayName = "Aluno Teste" }, ValidPassword);

        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        return email;
    }

    private async Task<ApplicationUser?> FindUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByEmailAsync(email);
    }
}
