using System.Net;
using System.Text.RegularExpressions;
using DevPath.Application.Content;
using DevPath.Application.Journey;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Persistence;
using DevPath.IntegrationTests.Infrastructure;
using DevPath.Web.Models.Lessons;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Progress;

/// <summary>PostgreSQL do Testcontainers com o /content REAL semeado pelo IContentSeeder da Web (#42).</summary>
public sealed class SeededContentWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DevPath", _postgres.GetConnectionString());
        builder.UseEnvironment("Testing");
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevPathDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IContentSeeder>().SeedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>
/// Módulo 1 de SQL &amp; Dados com o conteúdo do repositório: a tela mostra o subtítulo do path.yaml e as 5 frases
/// de completion_summary, na ordem das aulas (seed → IModuleCompletionContentService → adapter da Web).
/// </summary>
public sealed class ModuleCompletionSeededContentTests(SeededContentWebFactory factory) : IClassFixture<SeededContentWebFactory>
{
    private const string PathSlug = "sql-dados";
    private const string Module1 = "primeiros-passos-com-sql";

    public static readonly string[] Learned =
    [
        "Pedir dados a uma tabela com SELECT e FROM e ler o resultado como uma planilha.",
        "Ficar só com as linhas que interessam, com WHERE, e juntar condições com AND.",
        "Colocar o resultado em ordem com ORDER BY e pegar só as primeiras linhas com LIMIT.",
        "Juntar duas tabelas com INNER JOIN, como cada livro com o nome do seu autor.",
        "Resumir dados com COUNT, SUM e AVG e fazer um resumo por grupo com GROUP BY.",
    ];

    private const string Subtitle = "Você fez um banco de dados de verdade responder às suas perguntas, direto no navegador.";

    [Fact]
    public async Task Modulo1Concluido_MostraSubtitulo_EAsCincoFrasesDoConteudo_NaOrdem()
    {
        var (client, userId) = await StudentAsync();
        await CompleteModule1Async(userId);

        var response = await client.GetAsync($"/trilhas/{PathSlug}/modulos/{Module1}/concluido");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.ReadHtmlAsync();
        html.Should().Contain("Módulo concluído: Primeiros passos com SQL");
        html.Should().Contain($"data-module-subtitle>{Subtitle}</p>");
        html.Should().MatchRegex(@"<h2[^>]*>O que você aprendeu</h2>");
        var items = Regex.Matches(
                Regex.Match(html, @"<ol[^>]*data-module-learned>(.*?)</ol>", RegexOptions.Singleline).Groups[1].Value,
                @"<li class=""break-words"">(.*?)</li>")
            .Select(m => m.Groups[1].Value).ToList();
        items.Should().Equal(Learned);
        html.Should().Contain("+50 pontos neste módulo · Trilha SQL & Dados: 100%", "pontos e % do servidor");
        html.Should().Contain("data-next-module-coming-soon>O próximo módulo está chegando. Seu progresso fica guardado.</p>",
            "o módulo seguinte (ambiente) só tem aulas Em breve");
        html.Should().NotContain(ModuleCompletionTexts.EverythingDone);
        html.Should().NotContain("data-next-module>");
    }

    // Conclui as 5 aulas do módulo 1 direto no banco (o fluxo de clique/Executar já tem testes próprios).
    private async Task CompleteModule1Async(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        var lessons = await (
                from l in db.Lessons
                join m in db.Modules on l.ModuleId equals m.Id
                join p in db.CareerPaths on m.CareerPathId equals p.Id
                where p.Slug == PathSlug && m.Slug == Module1 && !l.IsDraft && l.IsPublished
                orderby l.Order
                select l.Id)
            .ToListAsync();
        lessons.Should().HaveCount(5);
        foreach (var id in lessons)
        {
            var progress = UserProgress.StartLesson(userId, id);
            progress.Complete(10, LessonCompletionKind.ExercisePassed);
            db.UserProgress.Add(progress);
        }
        await db.SaveChangesAsync();
    }

    private async Task<(HttpClient Client, Guid UserId)> StudentAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost")
        });
        var email = $"{Guid.NewGuid():N}@devpath.test";
        var register = await client.SubmitFormAsync("/Account/Register", "/Account/Register", new Dictionary<string, string>
        {
            ["DisplayName"] = "Aluna Conteúdo",
            ["Email"] = email,
            ["Password"] = "Senha123",
            ["ConfirmPassword"] = "Senha123"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Redirect);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var journey = scope.ServiceProvider.GetRequiredService<IStudentJourneyService>();
        (await journey.EnrollAsync(userId, PathSlug)).Kind.Should().Be(EnrollmentResultKind.Enrolled);
        return (client, userId);
    }
}
