using System.Net;
using System.Text.RegularExpressions;
using DevPath.Application.Progress;
using DevPath.Infrastructure.Identity;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.IntegrationTests.Onboarding;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.IntegrationTests.Lessons;

/// <summary>
/// Acesso real da página da aula (#27 / #30 B3): Locked e OtherPath = 403 Unavailable;
/// sem trilha = redirect para a escolha; "Continuar" no dashboard aponta pela Q6.
/// Usa o serviço real (JourneyApp) — o dublê AllowAll fica só em LessonPageTests.
/// </summary>
[Collection(JourneyAppCollection.Name)]
public sealed class LessonAccessPageTests(JourneyWebApplicationFactory factory)
{
    private const string Password = "Senha123";
    private const string Module = "primeiros-passos";

    private string FirstLessonUrl => $"/trilhas/{factory.SqlSlug}/{Module}/primeira-aula";
    private string SecondLessonUrl => $"/trilhas/{factory.SqlSlug}/{Module}/segunda-aula";
    private string BackendLessonUrl => $"/trilhas/{factory.BackendSlug}/fundamentos/variaveis";

    [Fact]
    public async Task AulaBloqueada_403ComOQueFaltaConcluir()
    {
        var client = factory.CreateBrowserClient();
        await RegisterAndEnrollAsync(client, factory.SqlSlug);

        var response = await client.GetAsync(SecondLessonUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var html = await response.ReadHtmlAsync();
        html.Should().Contain("Aula bloqueada")
            .And.Contain("Primeira Aula")
            .And.Contain("data-lesson-error");
    }

    [Fact]
    public async Task AulaDeOutraTrilha_403()
    {
        var client = factory.CreateBrowserClient();
        await RegisterAndEnrollAsync(client, factory.SqlSlug);

        var response = await client.GetAsync(BackendLessonUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var html = await response.ReadHtmlAsync();
        html.Should().Contain("Aula de outra trilha")
            .And.Contain("SQL Teste")
            .And.Contain("data-lesson-error");
    }

    [Fact]
    public async Task SemTrilha_RedirecionaParaAEscolha()
    {
        var client = factory.CreateBrowserClient();
        await RegisterAsync(client);

        var response = await client.GetAsync(FirstLessonUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/escolha-sua-trilha");
    }

    [Fact]
    public async Task Continuar_DepoisDeConcluirAPrimeira_ApontaParaASegunda()
    {
        var client = factory.CreateBrowserClient();
        var email = await RegisterAndEnrollAsync(client, factory.SqlSlug);

        var before = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();
        before.Should().MatchRegex(
            $@"href=""{Regex.Escape(FirstLessonUrl)}""[^>]*data-continue");

        await CompleteLessonAsync(email, factory.SqlSlug, Module, "primeira-aula");

        var after = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();
        after.Should().MatchRegex(
            $@"href=""{Regex.Escape(SecondLessonUrl)}""[^>]*data-continue>\s*Continuar");
    }

    [Fact]
    public async Task Continuar_AoAbrirAPrimeira_ContinuaNela_EmAndamento()
    {
        var client = factory.CreateBrowserClient();
        await RegisterAndEnrollAsync(client, factory.SqlSlug);

        (await client.GetAsync(FirstLessonUrl)).StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();
        html.Should().MatchRegex(
            $@"href=""{Regex.Escape(FirstLessonUrl)}""[^>]*data-continue>\s*Continuar");
    }

    private async Task CompleteLessonAsync(string email, string pathSlug, string moduleSlug, string lessonSlug)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        user.Should().NotBeNull();
        var progress = scope.ServiceProvider.GetRequiredService<ILessonProgressService>();
        var result = await progress.CompleteAsync(user!.Id, pathSlug, moduleSlug, lessonSlug);
        result.Kind.Should().Be(LessonCompletionResultKind.Completed);
    }

    private async Task<string> RegisterAndEnrollAsync(HttpClient client, string pathSlug)
    {
        var email = await RegisterAsync(client);
        var post = await client.SubmitFormAsync("/escolha-sua-trilha", "/escolha-sua-trilha",
            new Dictionary<string, string> { ["pathSlug"] = pathSlug });
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Be("/Dashboard");
        return email;
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var email = $"acesso-{Guid.NewGuid():N}@devpath.test";
        var response = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Aluna Acesso",
            ["Email"] = email,
            ["Password"] = Password,
            ["ConfirmPassword"] = Password
        });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return email;
    }
}
