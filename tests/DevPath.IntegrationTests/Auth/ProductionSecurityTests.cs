using System.Net;
using System.Text.RegularExpressions;
using DevPath.Infrastructure.Identity;
using DevPath.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.IntegrationTests.Auth;

/// <summary>
/// Configuração de produção atrás de proxy (QA da issue #9): cookie Secure, HSTS e
/// X-Forwarded-Proto aceito só de proxy confiável.
/// </summary>
[Collection(ProductionAppCollection.Name)]
public sealed partial class ProductionSecurityTests(ProductionBehindProxyFactory factory)
{
    private const string Password = "Senha123";

    [Fact]
    public async Task AtrasDeProxyHttps_LoginGeraCookieSecureEHsts()
    {
        var email = await CreateUserAsync();
        var client = factory.CreateProxyClient();

        // GET do formulário "vindo" do proxy em HTTPS: não redireciona e já manda HSTS.
        var page = await client.SendAsync(ViaProxy(HttpMethod.Get, "/Account/Login"));
        page.StatusCode.Should().Be(HttpStatusCode.OK, "com X-Forwarded-Proto: https a app sabe que já está em HTTPS");
        page.Headers.Contains("Strict-Transport-Security").Should().BeTrue();

        // POST do login com o token e o cookie antiforgery (cookies à mão: o jar não reenvia
        // cookies Secure para http://, e aqui a app acha que é https).
        var html = await page.Content.ReadAsStringAsync();
        var token = WebUtility.HtmlDecode(TokenRegex().Match(html).Groups[1].Value);
        var antiforgeryCookie = page.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Antiforgery")).Split(';')[0];

        var post = ViaProxy(HttpMethod.Post, "/Account/Login");
        post.Headers.Add("Cookie", antiforgeryCookie);
        post.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = Password,
            ["__RequestVerificationToken"] = token
        });

        var response = await client.SendAsync(post);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Contains("Strict-Transport-Security").Should().BeTrue();
        var authCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".DevPath.Auth="));
        authCookie.Should().ContainEquivalentOf("; secure").And.ContainEquivalentOf("httponly");
    }

    [Fact]
    public async Task XForwardedProtoDeIpDesconhecido_EhIgnoradoERedirecionaParaHttps()
    {
        var client = factory.CreateProxyClient();
        var request = ViaProxy(HttpMethod.Get, "/Account/Login");
        request.Headers.Add(ProductionBehindProxyFactory.SimulatedRemoteIpHeader, "203.0.113.9");

        var response = await client.SendAsync(request);

        // Cliente qualquer não pode "fingir" HTTPS: a app vê HTTP e redireciona para a porta 443.
        response.StatusCode.Should().Be(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location!.ToString().Should().Be("https://devpath.example/Account/Login");
    }

    private static HttpRequestMessage ViaProxy(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Forwarded-Proto", "https");
        return request;
    }

    private async Task<string> CreateUserAsync()
    {
        var email = $"prod-{Guid.NewGuid():N}@devpath.test";
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await userManager.CreateAsync(new ApplicationUser { UserName = email, Email = email, DisplayName = "Prod" }, Password);
        result.Succeeded.Should().BeTrue();
        return email;
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""")]
    private static partial Regex TokenRegex();
}
