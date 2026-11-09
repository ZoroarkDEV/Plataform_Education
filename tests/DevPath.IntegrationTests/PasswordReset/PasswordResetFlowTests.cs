using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using DevPath.Application.Email;
using DevPath.Infrastructure.Identity;
using DevPath.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.IntegrationTests.PasswordReset;

/// <summary>Recuperação de senha (#44, US-AUTH.1) de ponta a ponta: HTTP → fila → e-mail → nova senha.</summary>
[Collection(PasswordResetCollection.Name)]
public sealed partial class PasswordResetFlowTests(PasswordResetWebApplicationFactory factory)
{
    private const string OldPassword = "Senha123";
    private const string NewPassword = "NovaSenha456";
    private const string SentMessage =
        "Se este e-mail tiver uma conta, enviamos um link para criar uma senha nova. Confira também o spam.";
    private const string InvalidLinkMessage = "Este link não vale mais. Peça um novo.";

    [GeneratedRegex(@"https://devpath\.test/Account/ResetPassword\?userId=[0-9a-f-]{36}&token=[A-Za-z0-9_-]+")]
    private static partial Regex LinkRegex();

    [Fact]
    public async Task Forgot_EmailComContaESemConta_MesmaRespostaESoQuemTemContaRecebe()
    {
        var known = await CreateUserAsync();
        var unknown = NewEmail();
        var client = factory.CreateClientFromNewIp();

        var unknownResponse = await RequestResetAsync(client, unknown);
        var knownResponse = await RequestResetAsync(client, known);

        unknownResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        knownResponse.StatusCode.Should().Be(unknownResponse.StatusCode);
        knownResponse.Headers.Location.Should().Be(unknownResponse.Headers.Location);
        (await knownResponse.Content.ReadAsStringAsync()).Should().Be(await unknownResponse.Content.ReadAsStringAsync());

        var confirmation = await client.GetAsync(knownResponse.Headers.Location);
        (await confirmation.ReadHtmlAsync()).Should().Contain(SentMessage);

        // A fila é processada em ordem: quando o e-mail do "known" chega, o "unknown" já foi tratado.
        await factory.Emails.WaitForAsync(known);
        factory.Emails.SentTo(unknown).Should().BeEmpty();
    }

    [Fact]
    public async Task Forgot_ContaExistente_NaoEsperaOEnvioDoEmail()
    {
        // Prova de "mesmo tempo": o POST não depende do SMTP (que só acontece para quem tem conta).
        var known = await CreateUserAsync();
        var client = factory.CreateClientFromNewIp();
        factory.Emails.Delay = TimeSpan.FromSeconds(2);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await RequestResetAsync(client, known);
            stopwatch.Stop();

            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "o envio é em segundo plano");
            await factory.Emails.WaitForAsync(known);
        }
        finally
        {
            factory.Emails.Delay = TimeSpan.Zero;
        }
    }

    [Fact]
    public async Task Email_EmPortugues_HtmlETexto_LinkAbsolutoDaUrlPublicaENaoDoHost()
    {
        var known = await CreateUserAsync(displayName: "Ana <b>Aluna</b>");
        var client = factory.CreateClientFromNewIp();

        var page = await client.GetAsync("/Account/ForgotPassword");
        var token = AntiforgeryToken(await page.Content.ReadAsStringAsync());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/ForgotPassword")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = known, ["__RequestVerificationToken"] = token
            })
        };
        request.Headers.Host = "site-do-atacante.example";
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        var email = await factory.Emails.WaitForAsync(known);
        email.Subject.Should().Be("DevPath: crie sua nova senha");
        email.TextBody.Should().Contain("O link vale por 1 hora e só pode ser usado uma vez.");
        email.HtmlBody.Should().Contain("lang=\"pt-BR\"").And.Contain("Criar nova senha");
        email.HtmlBody.Should().Contain("Ana &lt;b&gt;Aluna&lt;/b&gt;").And.NotContain("<b>Aluna</b>");
        LinkRegex().IsMatch(email.TextBody).Should().BeTrue(email.TextBody);
        LinkRegex().IsMatch(WebUtility.HtmlDecode(email.HtmlBody)).Should().BeTrue();
        (email.TextBody + email.HtmlBody).Should().NotContain("site-do-atacante").And.NotContain("localhost");
    }

    [Fact]
    public async Task Reset_ComLinkValido_TrocaSenhaVaiParaEntrarComMensagemEEntraComASenhaNova()
    {
        var known = await CreateUserAsync();
        var client = factory.CreateClientFromNewIp();
        var link = await RequestLinkAsync(client, known);

        var form = await client.GetAsync(link);
        form.StatusCode.Should().Be(HttpStatusCode.OK);
        form.Headers.GetValues("Referrer-Policy").Should().Contain("no-referrer");
        (await form.ReadHtmlAsync()).Should().Contain("Criar nova senha").And.Contain("novalidate");

        var response = await SubmitNewPasswordAsync(client, link, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Account/Login");
        var login = await client.GetAsync("/Account/Login");
        (await login.ReadHtmlAsync()).Should().Contain("Senha alterada. Você já pode entrar.");

        (await LoginAsync(factory.CreateClientFromNewIp(), known, OldPassword)).StatusCode
            .Should().Be(HttpStatusCode.OK, "a senha antiga não vale mais");
        var newLogin = await LoginAsync(client, known, NewPassword);
        newLogin.StatusCode.Should().Be(HttpStatusCode.Redirect);
        newLogin.Headers.Location!.ToString().Should().NotContain("/Account/Login");
    }

    [Fact]
    public async Task Reset_MesmoLinkDuasVezes_SegundaVezDizQueOLinkNaoVale()
    {
        var known = await CreateUserAsync();
        var client = factory.CreateClientFromNewIp();
        var link = await RequestLinkAsync(client, known);
        var (userId, token) = ParseLink(link);

        // Pega o antiforgery antes de usar o link (depois ele mostra a tela de link inválido).
        var page = await client.GetAsync(link);
        var antiforgery = AntiforgeryToken(await page.Content.ReadAsStringAsync());

        (await SubmitNewPasswordAsync(client, link, NewPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        (await (await client.GetAsync(link)).ReadHtmlAsync()).Should().Contain(InvalidLinkMessage);
        var reuse = await client.PostAsync("/Account/ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserId"] = userId, ["Token"] = token, ["Password"] = "Terceira789", ["ConfirmPassword"] = "Terceira789",
            ["__RequestVerificationToken"] = antiforgery
        }));
        reuse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reuse.ReadHtmlAsync()).Should().Contain(InvalidLinkMessage).And.Contain("Pedir um novo link");

        (await CheckPasswordAsync(known, NewPassword)).Should().BeTrue("o segundo uso não troca a senha");
    }

    [Fact]
    public async Task Reset_LinkAnteriorAoDaTroca_TambemDeixaDeValer()
    {
        var known = await CreateUserAsync();
        var client = factory.CreateClientFromNewIp();
        var first = await RequestLinkAsync(client, known);
        var second = await RequestLinkAsync(client, known, count: 2);

        (await SubmitNewPasswordAsync(client, second, NewPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        (await (await client.GetAsync(first)).ReadHtmlAsync()).Should().Contain(InvalidLinkMessage);
    }

    [Fact]
    public async Task Reset_LinkVencido_DizQueOLinkNaoVale()
    {
        await using var shortLived = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Identity:PasswordResetTokenLifespan", "00:00:01"));
        var emails = shortLived.Services.GetRequiredService<CapturingEmailSender>();
        var known = await CreateUserAsync();
        var client = PasswordResetWebApplicationFactory.WithNewIp(shortLived.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost")
        }));

        (await RequestResetAsync(client, known)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        var link = ToRelative(LinkRegex().Match((await emails.WaitForAsync(known)).TextBody).Value);
        var page = await client.GetAsync(link);
        var antiforgery = AntiforgeryToken(await page.Content.ReadAsStringAsync());

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        (await (await client.GetAsync(link)).ReadHtmlAsync()).Should().Contain(InvalidLinkMessage);
        var (userId, token) = ParseLink(link);
        var post = await client.PostAsync("/Account/ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserId"] = userId, ["Token"] = token, ["Password"] = NewPassword, ["ConfirmPassword"] = NewPassword,
            ["__RequestVerificationToken"] = antiforgery
        }));
        (await post.ReadHtmlAsync()).Should().Contain(InvalidLinkMessage);
        (await CheckPasswordAsync(known, OldPassword)).Should().BeTrue();
    }

    [Fact]
    public async Task Reset_LinkAdulteradoOuIncompleto_DizQueOLinkNaoVale()
    {
        var client = factory.CreateClientFromNewIp();

        foreach (var url in new[]
                 {
                     "/Account/ResetPassword",
                     $"/Account/ResetPassword?userId={Guid.NewGuid()}&token=abc",
                     "/Account/ResetPassword?userId=nao-e-guid&token=%%%"
                 })
        {
            var response = await client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK, url);
            (await response.ReadHtmlAsync()).Should().Contain(InvalidLinkMessage, url);
        }
    }

    [Fact]
    public async Task Reset_SenhaFraca_MostraARegraDoCadastroEmPortuguesENaoGastaOLink()
    {
        var known = await CreateUserAsync();
        var client = factory.CreateClientFromNewIp();
        var link = await RequestLinkAsync(client, known);

        var weak = await SubmitNewPasswordAsync(client, link, "abc123");

        weak.StatusCode.Should().Be(HttpStatusCode.OK);
        (await weak.ReadHtmlAsync()).Should()
            .Contain("A senha precisa ter pelo menos 8 caracteres, com letra maiúscula, letra minúscula e número.");
        (await SubmitNewPasswordAsync(client, link, NewPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Reset_RegraDoIdentity_TambemEmPortugues()
    {
        // Passa no ViewModel (8+, maiúscula, minúscula, número) mas o validador do Identity é a fonte da
        // verdade: aqui conferimos que o erro DELE também sai em português, chamando o serviço direto.
        var known = await CreateUserAsync();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(known))!;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var service = scope.ServiceProvider.GetRequiredService<DevPath.Application.Auth.IPasswordResetService>();
        var (_, encoded) = ParseLink(ToRelative(PasswordResetService.BuildLink(new Uri("https://devpath.test"), user.Id, token)));

        var result = await service.ResetPasswordAsync(new DevPath.Application.Auth.ResetPasswordRequest(
            user.Id.ToString(), encoded, "semnumero"));

        result.Succeeded.Should().BeFalse();
        result.InvalidLink.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be("A senha precisa ter pelo menos 8 caracteres, com letra maiúscula, letra minúscula e número.");
    }

    [Fact]
    public async Task Reset_EncerraAsOutrasSessoesAbertas()
    {
        var known = await CreateUserAsync();
        var otherBrowser = factory.CreateClientFromNewIp();
        (await LoginAsync(otherBrowser, known, OldPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await otherBrowser.GetAsync("/dashboard")).Headers.Location?.ToString()
            .Should().NotContain("/Account/Login", "a sessão está aberta antes da troca");

        var client = factory.CreateClientFromNewIp();
        var link = await RequestLinkAsync(client, known);
        (await SubmitNewPasswordAsync(client, link, NewPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        var after = await otherBrowser.GetAsync("/dashboard");
        after.StatusCode.Should().Be(HttpStatusCode.Redirect);
        after.Headers.Location!.ToString().Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task Reset_ContaBloqueadaPorSenhasErradas_VoltaAEntrar()
    {
        var known = await CreateUserAsync();
        var attacker = factory.CreateClientFromNewIp();
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(attacker, known, "Errada123");
        }

        var client = factory.CreateClientFromNewIp();
        var link = await RequestLinkAsync(client, known);
        (await SubmitNewPasswordAsync(client, link, NewPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        (await LoginAsync(client, known, NewPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Worker_ProvedorRecusa_NaoDerrubaAFilaEOProximoEmailSai()
    {
        var first = await CreateUserAsync();
        var second = await CreateUserAsync();
        factory.Emails.FailNext = new DevPath.Infrastructure.Email.EmailDeliveryException("Resend respondeu HTTP 500.");

        await RequestResetAsync(factory.CreateClientFromNewIp(), first);
        await RequestResetAsync(factory.CreateClientFromNewIp(), second);

        await factory.Emails.WaitForAsync(second);
        factory.Emails.SentTo(first).Should().BeEmpty();
        factory.Emails.FailNext.Should().BeNull();
    }

    [Fact]
    public async Task Forgot_SemAntiforgery_400ENadaEnviado()
    {
        var known = await CreateUserAsync();
        var client = factory.CreateClientFromNewIp();

        var response = await client.PostAsync("/Account/ForgotPassword",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = known }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await Task.Delay(500);
        factory.Emails.SentTo(known).Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_SemAntiforgery_400ESenhaIntacta()
    {
        var known = await CreateUserAsync();
        var client = factory.CreateClientFromNewIp();
        var (userId, token) = ParseLink(await RequestLinkAsync(client, known));

        var response = await client.PostAsync("/Account/ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserId"] = userId, ["Token"] = token, ["Password"] = NewPassword, ["ConfirmPassword"] = NewPassword
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CheckPasswordAsync(known, OldPassword)).Should().BeTrue();
    }

    [Fact]
    public async Task Forgot_LimitePorEmail_QuartoPedidoTemAMesmaRespostaMasNaoEnvia()
    {
        var known = await CreateUserAsync();
        var sentinel = await CreateUserAsync();

        for (var i = 1; i <= 4; i++)
        {
            // IP novo a cada pedido: só o limite por e-mail (3/h) entra em jogo.
            var response = await RequestResetAsync(factory.CreateClientFromNewIp(), known);
            response.StatusCode.Should().Be(HttpStatusCode.Redirect, $"pedido {i}");
            response.Headers.Location!.ToString().Should().Be("/Account/ForgotPasswordConfirmation");
        }

        await RequestResetAsync(factory.CreateClientFromNewIp(), sentinel);
        await factory.Emails.WaitForAsync(sentinel);
        factory.Emails.SentTo(known).Should().HaveCount(3);
    }

    [Fact]
    public async Task Forgot_LimitePorIp_Devolve429ComMensagemEmPortugues()
    {
        var client = factory.CreateClientFromNewIp();
        for (var i = 0; i < PasswordResetWebApplicationFactory.PerIpLimit; i++)
        {
            (await RequestResetAsync(client, NewEmail())).StatusCode.Should().Be(HttpStatusCode.Redirect);
        }

        var blocked = await RequestResetAsync(client, NewEmail());

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await blocked.ReadHtmlAsync()).Should()
            .Contain("Você fez muitos pedidos seguidos. Espere alguns minutos e tente de novo.");
        (await RequestResetAsync(factory.CreateClientFromNewIp(), NewEmail())).StatusCode
            .Should().Be(HttpStatusCode.Redirect, "outro IP não é afetado");
    }

    [Fact]
    public async Task Login_TemLinkEsqueciMinhaSenha_ECadastroDuplicadoOfereceRecuperar()
    {
        var client = factory.CreateClientFromNewIp();

        var login = await (await client.GetAsync("/Account/Login")).ReadHtmlAsync();
        login.Should().Contain("href=\"/Account/ForgotPassword\"").And.Contain("Esqueci minha senha");

        var known = await CreateUserAsync();
        var register = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Ana", ["Email"] = known, ["Password"] = OldPassword, ["ConfirmPassword"] = OldPassword
        });
        (await register.ReadHtmlAsync()).Should().Contain("recuperar o acesso");
    }

    // ---------- helpers ----------

    private static string NewEmail() => $"aluno-{Guid.NewGuid():N}@devpath.test";

    private static Task<HttpResponseMessage> RequestResetAsync(HttpClient client, string email) =>
        client.SubmitFormAsync("/Account/ForgotPassword", "/Account/ForgotPassword",
            new Dictionary<string, string> { ["Email"] = email });

    private async Task<string> RequestLinkAsync(HttpClient client, string email, int count = 1)
    {
        (await RequestResetAsync(client, email)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        var message = await factory.Emails.WaitForAsync(email, count);
        return ToRelative(LinkRegex().Match(message.TextBody).Value);
    }

    private static string ToRelative(string absolute) => new Uri(absolute).PathAndQuery;

    private static (string UserId, string Token) ParseLink(string relative)
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("https://x" + relative).Query);
        return (query["userId"].ToString(), query["token"].ToString());
    }

    private static Task<HttpResponseMessage> SubmitNewPasswordAsync(HttpClient client, string link, string password)
    {
        var (userId, token) = ParseLink(link);
        return client.SubmitFormAsync(link, "/Account/ResetPassword", new Dictionary<string, string>
        {
            ["UserId"] = userId, ["Token"] = token, ["Password"] = password, ["ConfirmPassword"] = password
        });
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.SubmitFormAsync("/Account/Login", "/Account/Login",
            new Dictionary<string, string> { ["Email"] = email, ["Password"] = password });

    private static string AntiforgeryToken(string html) =>
        WebUtility.HtmlDecode(Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""").Groups[1].Value);

    private async Task<string> CreateUserAsync(string displayName = "Aluno Teste")
    {
        var email = NewEmail();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await users.CreateAsync(
            new ApplicationUser { UserName = email, Email = email, DisplayName = displayName }, OldPassword);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        return email;
    }

    private async Task<bool> CheckPasswordAsync(string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.CheckPasswordAsync((await users.FindByEmailAsync(email))!, password);
    }
}
