using System.Net;
using DevPath.Application.Journey;
using DevPath.Domain.CareerPaths;
using DevPath.Infrastructure.Persistence;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.Web.Models.Lessons;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Progress;

/// <summary>
/// Duas trilhas no PostgreSQL do Testcontainers (#42): "com próximo" (módulo 1 → módulo 2 pronto) e
/// "sem próximo" (módulo 1 → módulo só com rascunhos, como hoje antes do #41).
/// </summary>
public sealed class ModuleCompletionWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public (string Path, string Module1, string Lesson1, string Module2, string Lesson2) WithNext { get; private set; }
    public (string Path, string Module1, string Lesson1) WithoutNext { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DevPath", _postgres.GetConnectionString());
        builder.UseEnvironment("Testing");
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        await db.Database.MigrateAsync();

        var a = CareerPath.Create($"Trilha com próximo {Guid.NewGuid():N}");
        var a1 = a.AddModule("Fundamentos");
        a1.Publish();
        var a1l = a1.AddLesson("Leitura um", ContentType.Markdown, "# Leitura um\n\nTexto.");
        a1.AddLesson("Rascunho do um", ContentType.Markdown, "# Rascunho\n\nAula em construção.", isDraft: true);
        var a2 = a.AddModule("Segundo módulo");
        a2.Publish();
        var a2l = a2.AddLesson("Leitura dois", ContentType.Markdown, "# Leitura dois\n\nTexto.");

        var b = CareerPath.Create($"Trilha sem próximo {Guid.NewGuid():N}");
        var b1 = b.AddModule("Único pronto");
        b1.Publish();
        var b1l = b1.AddLesson("Leitura só", ContentType.Markdown, "# Leitura só\n\nTexto.");
        var b2 = b.AddModule("Em construção");
        b2.Publish();
        b2.AddLesson("Rascunho", ContentType.Markdown, "# Rascunho\n\nAula em construção.", isDraft: true);

        db.CareerPaths.AddRange(a, b);
        await db.SaveChangesAsync();
        WithNext = (a.Slug, a1.Slug, a1l.Slug, a2.Slug, a2l.Slug);
        WithoutNext = (b.Slug, b1.Slug, b1l.Slug);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>Tela "Módulo concluído" com o progresso e o dashboard REAIS (#42).</summary>
public sealed class ModuleCompletionRealServiceTests(ModuleCompletionWebFactory factory) : IClassFixture<ModuleCompletionWebFactory>
{
    private static string Url(string path, string module, string lesson) => $"/trilhas/{path}/{module}/{lesson}";
    private static string Summary(string path, string module) => $"/trilhas/{path}/modulos/{module}/concluido";

    [Fact]
    public async Task UltimaAulaProntaDoModulo_LevaATela_ComBotaoParaOModulo2_EDashboardMarcaConcluido()
    {
        var (path, m1, l1, m2, l2) = factory.WithNext;
        var client = await StudentAsync(path);
        var lesson = Url(path, m1, l1);

        (await client.GetAsync(Summary(path, m1))).Headers.Location!.ToString()
            .Should().StartWith("/Dashboard", "antes de concluir não há resumo");

        var post = await client.SubmitFormAsync(lesson, lesson + "/concluir", new Dictionary<string, string>());
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Be(Summary(path, m1), "o rascunho do módulo não conta (R4)");

        var page = await client.GetAsync(Summary(path, m1));
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await page.ReadHtmlAsync();
        html.Should().Contain("Módulo concluído: Fundamentos");
        html.Should().MatchRegex(@"\+0 pontos neste módulo · Trilha Trilha com próximo [0-9a-f]+: 50%", "aula de leitura não vale pontos; % do servidor");
        html.Should().Contain($"href=\"{Url(path, m2, l2)}\"").And.Contain("Começar o módulo 2: Segundo módulo");
        html.Should().NotContain("O que você aprendeu", "o seed ainda não grava completion_summary");

        var again = await client.SubmitFormAsync(lesson, lesson + "/concluir", new Dictionary<string, string>());
        again.Headers.Location!.ToString().Should().Be(lesson, "refazer a aula não mostra a tela de novo");

        var dashboard = await (await client.GetAsync("/dashboard")).ReadHtmlAsync();
        dashboard.Should().Contain($"href=\"{Summary(path, m1)}\"").And.Contain("Concluído ✓");
        dashboard.Should().Contain("Próximo passo: módulo 2, Segundo módulo");
    }

    [Fact]
    public async Task SemModuloSeguintePronto_TextoDeTudoPronto_SemBotao()
    {
        var (path, m1, l1) = factory.WithoutNext;
        var client = await StudentAsync(path);
        var lesson = Url(path, m1, l1);

        var post = await client.SubmitFormAsync(lesson, lesson + "/concluir", new Dictionary<string, string>());
        post.Headers.Location!.ToString().Should().Be(Summary(path, m1));

        var html = await (await client.GetAsync(Summary(path, m1))).ReadHtmlAsync();
        html.Should().Contain(ModuleCompletionTexts.EverythingDone);
        html.Should().NotContain("data-next-module");
        html.Should().NotContain("Começar o módulo");
        html.Should().MatchRegex(@"Trilha Trilha sem próximo [0-9a-f]+: 100%");
    }

    [Fact]
    public async Task ResumoDeModuloDeOutraTrilha_404()
    {
        var client = await StudentAsync(factory.WithNext.Path);

        var response = await client.GetAsync(Summary(factory.WithoutNext.Path, factory.WithoutNext.Module1));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<HttpClient> StudentAsync(string pathSlug)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost")
        });
        var email = $"{Guid.NewGuid():N}@devpath.test";
        var register = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Aluna Módulo",
            ["Email"] = email,
            ["Password"] = "Senha123",
            ["ConfirmPassword"] = "Senha123"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Redirect);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var journey = scope.ServiceProvider.GetRequiredService<IStudentJourneyService>();
        (await journey.EnrollAsync(userId, pathSlug)).Kind.Should().Be(EnrollmentResultKind.Enrolled);
        return client;
    }
}
