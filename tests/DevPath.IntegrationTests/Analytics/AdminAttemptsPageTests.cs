using System.Net;
using DevPath.Application.Analytics;
using DevPath.Infrastructure.Identity;
using DevPath.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.IntegrationTests.Analytics;

/// <summary>/admin/tentativas (#43): só o papel Admin vê; aluno logado recebe 404 e anônimo vai para o login.</summary>
[Collection(WebAppCollection.Name)]
public sealed class AdminAttemptsPageTests(DevPathWebApplicationFactory factory)
{
    private const string Url = "/admin/tentativas";
    private const string Password = "Senha123";

    [Fact]
    public async Task Anonimo_VaiParaOLogin()
    {
        var response = await factory.CreateBrowserClient().GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Account/Login?ReturnUrl=%2Fadmin%2Ftentativas");
    }

    [Fact]
    public async Task AlunoLogado_Recebe404_SemNenhumDado()
    {
        var (client, _) = await RegisterAsync();

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().NotContain("tabela-ate-acertar").And.NotContain("Tentativas do Executar");
    }

    [Fact]
    public async Task Admin_VeAsTresTabelas_SemCache()
    {
        var (_, email) = await RegisterAsync();
        await GrantAdminAsync(email);
        var client = await LoginAsync(email); // o papel vai no cookie: precisa entrar de novo

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var html = await response.ReadHtmlAsync();
        html.Should().Contain("tabela-ate-acertar").And.Contain("tabela-seguir-sem-pontos").And.Contain("tabela-taxa-falha");
        html.Should().Contain("Quantas tentativas até acertar?").And.Contain("Seguir sem pontos");
    }

    [Fact]
    public async Task PapelRemovido_PerdeOAcessoNoProximoLogin()
    {
        var (_, email) = await RegisterAsync();
        await GrantAdminAsync(email);
        (await (await LoginAsync(email)).GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            (await users.RemoveFromRoleAsync(user!, AdminAccess.Role)).Succeeded.Should().BeTrue();
        }

        (await (await LoginAsync(email)).GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(HttpClient Client, string Email)> RegisterAsync()
    {
        var client = factory.CreateBrowserClient();
        var email = $"admin-teste-{Guid.NewGuid():N}@devpath.test";
        var response = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Pessoa",
            ["Email"] = email,
            ["Password"] = Password,
            ["ConfirmPassword"] = Password
        });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return (client, email);
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = factory.CreateBrowserClient();
        var response = await client.SubmitFormAsync("/Account/Login", "/Account/Login", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = Password
        });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private async Task GrantAdminAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(AdminAccess.Role))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(AdminAccess.Role));
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        (await users.AddToRoleAsync(user!, AdminAccess.Role)).Succeeded.Should().BeTrue();
    }
}
