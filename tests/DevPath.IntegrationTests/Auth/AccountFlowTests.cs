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

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Dashboard");

        var dashboard = await client.GetAsync("/dashboard");
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        (await dashboard.ReadHtmlAsync()).Should().Contain("Olá, Ana Aluna!");

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
        (await response.ReadHtmlAsync()).Should().Contain("As senhas não conferem.");
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
        (await client.GetAsync("/dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);

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
    /// (~3 ms para e-mail inexistente contra ~55 ms para conta real, razão ~0,05).
    /// Exigimos apenas que o caminho "não existe" leve pelo menos 30% do caminho "senha errada".
    /// Medimos a mediana de várias tentativas para reduzir o ruído da máquina.
    /// </summary>
    [Fact]
    public async Task Login_EmailInexistente_LevaTempoParecidoComSenhaErrada()
    {
        var client = factory.CreateBrowserClient();
        var existing = await CreateUserAsync();

        async Task<double> MedianMsAsync(Func<Task> login)
        {
            await login(); // aquecimento (JIT, conexão com o banco)
            var samples = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                await login();
                samples.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            return samples.Order().ElementAt(samples.Count / 2);
        }

        // Senha errada em conta real: usamos uma conta nova a cada rodada para não chegar no bloqueio.
        var wrongPasswordMs = await MedianMsAsync(async () => await LoginAsync(client, await CreateUserAsync(), "SenhaErrada1"));
        var nonexistentMs = await MedianMsAsync(() => LoginAsync(client, NewEmail(), "SenhaErrada1"));

        nonexistentMs.Should().BeGreaterThan(wrongPasswordMs * 0.3,
            $"e-mail inexistente levou {nonexistentMs:F1} ms e senha errada {wrongPasswordMs:F1} ms");
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
