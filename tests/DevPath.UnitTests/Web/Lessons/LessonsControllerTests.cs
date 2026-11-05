using System.Security.Claims;
using DevPath.Application.Auth;
using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;
using FluentAssertions;
using DevPath.Application.Lessons;
using DevPath.Domain.Common;
using DevPath.Web.Controllers;
using DevPath.Web.Lessons;
using DevPath.Web.Models.Lessons;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevPath.UnitTests.Web.Lessons;

/// <summary>
/// Controller isolado, com um dublê do ILessonContentService (só nos testes) montado com as
/// fábricas do contrato do Backend (LessonLoadResult.Success/NotFound/Malformed/Draft).
/// </summary>
public sealed class LessonsControllerTests
{
    private const string Path = "sql-dados";
    private const string Module = "primeiros-passos";
    private const string FullSlug = "seu-primeiro-select";
    private const string DraftSlug = "filtrando-com-where";
    private const string MalformedSlug = "aula-com-defeito";
    private const string MalformedDetail = "A aula está sem conteúdo (corpo vazio).";

    private readonly ListLogger _logger = new();

    private static readonly LessonBreadcrumb Crumb = new(Path, "SQL & Dados", Module, "Primeiros passos");

    private static LessonDto FullLesson(ExerciseDto? exercise = null) => new(
        Guid.NewGuid(), FullSlug, "Seu primeiro SELECT", 15, DifficultyLevel.Beginner, "sql",
        ["Ler dados com SELECT"],
        "# Seu primeiro SELECT\n\n```sql\nSELECT 1;\n```\n\n```csharp\nvar x = 1;\n```\n\n## Resumo\n\nFim.",
        Crumb,
        new LessonLink("o-que-e-banco", "O que é banco", IsDraft: false),
        new LessonLink(DraftSlug, "Filtrando com WHERE", IsDraft: true),
        Position: 2, TotalInModule: 4, Exercise: exercise);

    private sealed class StubLessonService(ExerciseDto? exercise = null) : ILessonContentService
    {
        public Task<LessonLoadResult> GetLessonAsync(string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default) =>
            Task.FromResult((pathSlug, moduleSlug, lessonSlug) switch
            {
                (Path, Module, FullSlug) => LessonLoadResult.Success(FullLesson(exercise)),
                (Path, Module, DraftSlug) => LessonLoadResult.Draft("Esta aula ainda está em construção."),
                (Path, Module, MalformedSlug) => LessonLoadResult.Malformed(MalformedDetail),
                _ => LessonLoadResult.NotFound("Aula não encontrada."),
            });
    }

    private sealed class StubAuth : IAuthService
    {
        public Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthResult.Success());
        public Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthResult.Success());
        public Task LogoutAsync() => Task.CompletedTask;
    }

    private sealed class StubProgress : ILessonProgressGateway
    {
        public LessonAccessOutcome OpenResult { get; set; } = LessonAccessOutcome.Unavailable;
        public Task<LessonAccessOutcome> GetStateAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken) =>
            Task.FromResult(OpenResult);
        public Task<LessonAccessOutcome> OpenAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken cancellationToken) =>
            Task.FromResult(OpenResult);
        public Task<LessonCompletionOutcome> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, bool skipWithoutPoints, CancellationToken cancellationToken) =>
            Task.FromResult(new LessonCompletionOutcome(LessonCompletionStatus.Unavailable));
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private LessonsController CreateController(
        ILessonContentService? service = null, string environment = "Testing", ExerciseDto? exercise = null,
        ILessonProgressGateway? progress = null)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Name, "aluna@test"),
            ], "test"))
        };
        var controller = new LessonsController(
            service ?? new StubLessonService(exercise), new LessonMarkdownRenderer(),
            progress ?? new StubProgress(), new StubAuth(), new StubEnvironment(environment), _logger)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NullTempDataProvider()),
        };
        return controller;
    }

    [Fact]
    public void Controller_ExigeLogin() =>
        typeof(LessonsController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Should().NotBeEmpty();

    // Acesso (#27, portados do Backend a9672a2 para a porta ILessonProgressGateway).
    private static readonly LessonRef FirstLessonRef = new(Path, Module, "o-que-e-banco", "O que é banco");

    private static LessonProgressView LockedState() =>
        new(new LessonRef(Path, Module, FullSlug, "Seu primeiro SELECT"), LessonStatus.Locked, FirstLessonRef,
            null, null, null, false, null, null, null, null, 0);

    [Fact]
    public async Task Show_AulaBloqueada_403ComOQueFaltaConcluir()
    {
        var controller = CreateController(progress: new StubProgress { OpenResult = new(LessonAccessStatus.Locked, LockedState()) });

        var result = await controller.Show(Path, Module, FullSlug, CancellationToken.None);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be(LessonsController.UnavailableView);
        var model = view.Model.Should().BeOfType<LessonUnavailableViewModel>().Subject;
        model.Reason.Should().Be(LessonUnavailableReason.Locked);
        model.RequiredLessonTitle.Should().Be("O que é banco");
        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Show_AulaDeOutraTrilha_403()
    {
        var controller = CreateController(progress: new StubProgress { OpenResult = new(LessonAccessStatus.NotEnrolled, null, "Backend com .NET") });

        var result = await controller.Show(Path, Module, FullSlug, CancellationToken.None);

        var model = result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<LessonUnavailableViewModel>().Subject;
        model.Reason.Should().Be(LessonUnavailableReason.OtherPath);
        model.CurrentPathTitle.Should().Be("Backend com .NET");
        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Show_SemTrilha_VaiParaAEscolha()
    {
        var controller = CreateController(progress: new StubProgress { OpenResult = new(LessonAccessStatus.NotEnrolled) });

        var result = await controller.Show(Path, Module, FullSlug, CancellationToken.None);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ControllerName.Should().Be("Onboarding");
        redirect.ActionName.Should().Be(nameof(OnboardingController.ChoosePath));
    }

    [Fact]
    public async Task Show_AulaComDefeitoEBloqueada_403_NaoRevelaODefeito()
    {
        var controller = CreateController(progress: new StubProgress { OpenResult = new(LessonAccessStatus.Locked, LockedState()) });

        var result = await controller.Show(Path, Module, MalformedSlug, CancellationToken.None);

        result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<LessonUnavailableViewModel>()
            .Which.Reason.Should().Be(LessonUnavailableReason.Locked);
        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Show_RascunhoBloqueado_Continua404()
    {
        var controller = CreateController(progress: new StubProgress { OpenResult = new(LessonAccessStatus.Locked, LockedState()) });

        (await controller.Show(Path, Module, DraftSlug, CancellationToken.None)).Should().BeOfType<ViewResult>()
            .Which.Model.Should().BeOfType<LessonUnavailableViewModel>().Which.Reason.Should().Be(LessonUnavailableReason.Draft);
        controller.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Show_AulaExistente_DevolveViewComHtmlConvertido()
    {
        var controller = CreateController();

        var result = await controller.Show(Path, Module, FullSlug, CancellationToken.None);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().BeNull();
        var model = view.Model.Should().BeOfType<LessonPageViewModel>().Subject;
        model.Lesson.Title.Should().Be("Seu primeiro SELECT");
        model.LevelLabel.Should().Be("Iniciante");
        model.EstimatedTimeLabel.Should().Be("~15 min");
        model.PositionLabel.Should().Be("Aula 2 de 4");
        model.Html.ContentHtml.Should().Contain("language-sql").And.Contain("language-csharp");
        model.Exercise.Should().BeNull("sem exercício, a seção some");
        model.Html.SummaryHtml.Should().NotBeNull();
        model.Lesson.NextLesson!.IsDraft.Should().BeTrue();
        controller.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Show_AulaComDefeito_MostraMensagemAmigavelELogaErro()
    {
        var controller = CreateController();

        var result = await controller.Show(Path, Module, MalformedSlug, CancellationToken.None);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be(LessonsController.UnavailableView);
        var model = view.Model.Should().BeOfType<LessonUnavailableViewModel>().Subject;
        model.Reason.Should().Be(LessonUnavailableReason.Malformed);
        model.Message.Should().Be("Não conseguimos carregar esta aula agora. Tente de novo ou volte ao dashboard.");
        model.TechnicalDetail.Should().BeNull("fora de Development o detalhe técnico não vai para a tela");
        controller.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable,
            "Malformed devolve 503 + Unavailable; nunca 500");
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains(MalformedDetail));
    }

    [Fact]
    public async Task Show_EmDevelopment_DetalheTecnicoVaiRecolhidoParaATela()
    {
        var controller = CreateController(environment: Environments.Development);

        var result = await controller.Show(Path, Module, MalformedSlug, CancellationToken.None);

        var model = ((ViewResult)result).Model.Should().BeOfType<LessonUnavailableViewModel>().Subject;
        model.TechnicalDetail.Should().Be(MalformedDetail);
        controller.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable,
            "Malformed = 503 + Unavailable também em Development");
    }

    [Fact]
    public async Task Show_AulaInexistente_Devolve404Amigavel()
    {
        var controller = CreateController();

        var result = await controller.Show(Path, Module, "nao-existe", CancellationToken.None);

        var model = ((ViewResult)result).Model.Should().BeOfType<LessonUnavailableViewModel>().Subject;
        model.Reason.Should().Be(LessonUnavailableReason.NotFound);
        controller.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Show_Rascunho_NaoAbreEMostraEmBreve()
    {
        var controller = CreateController();

        var result = await controller.Show(Path, Module, DraftSlug, CancellationToken.None);

        var model = ((ViewResult)result).Model.Should().BeOfType<LessonUnavailableViewModel>().Subject;
        model.Reason.Should().Be(LessonUnavailableReason.Draft);
        model.Title.Should().Be("Aula em breve");
        model.Message.Should().StartWith("Esta aula está sendo escrita");
        controller.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Theory]
    [InlineData("sql_dados")]
    [InlineData("..")]
    [InlineData("a b")]
    public async Task Show_SlugInvalido_NemChamaOServico(string pathSlug)
    {
        var service = new ThrowingService();
        var controller = CreateController(service);

        var result = await controller.Show(pathSlug, Module, "x", CancellationToken.None);

        ((ViewResult)result).Model.Should().BeOfType<LessonUnavailableViewModel>()
            .Which.Reason.Should().Be(LessonUnavailableReason.NotFound);
        service.Called.Should().BeFalse();
    }

    [Fact]
    public async Task Show_ServicoLancaExcecao_MostraPaginaAmigavelELoga()
    {
        var controller = CreateController(new ThrowingService());

        var result = await controller.Show(Path, Module, "qualquer", CancellationToken.None);

        ((ViewResult)result).Model.Should().BeOfType<LessonUnavailableViewModel>()
            .Which.Reason.Should().Be(LessonUnavailableReason.Malformed);
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Show_SucessoComCorpoVazio_TrataComoDefeito()
    {
        var lesson = new LessonDto(Guid.NewGuid(), "x", "X", 5, DifficultyLevel.Advanced, null, [], "   ",
            Crumb, null, null, Position: 1, TotalInModule: 1);
        var controller = CreateController(new FixedService(LessonLoadResult.Success(lesson)));

        var result = await controller.Show(Path, Module, "x", CancellationToken.None);

        ((ViewResult)result).Model.Should().BeOfType<LessonUnavailableViewModel>()
            .Which.Reason.Should().Be(LessonUnavailableReason.Malformed);
    }

    [Fact]
    public async Task Show_SlugComMaiusculas_NormalizaParaMinusculas()
    {
        var result = await CreateController().Show("SQL-Dados", Module, "Seu-Primeiro-SELECT", CancellationToken.None);

        ((ViewResult)result).Model.Should().BeOfType<LessonPageViewModel>();
    }

    [Fact]
    public async Task Show_ComExercicio_MontaVisaoSeguraComMarkdownECodigoInicial()
    {
        var exercise = new ExerciseDto("Catálogo", "Liste o **título**.", "sql", "livraria",
            "SELECT\nFROM livros;", ["Geral", "", "Direta"]) { DatasetDisplayName = "Livraria" };

        var result = await CreateController(exercise: exercise).Show(Path, Module, FullSlug, CancellationToken.None);

        var model = ((ViewResult)result).Model.Should().BeOfType<LessonPageViewModel>().Subject;
        var vm = model.Exercise!;
        vm.Title.Should().Be("Catálogo");
        vm.StatementHtml.Should().Contain("<strong>título</strong>");
        vm.LanguageLabel.Should().Be("SQL");
        vm.DatasetSlug.Should().Be("livraria");
        vm.DatasetDisplayName.Should().Be("Livraria");
        vm.PointsLabel.Should().Be("10 pontos");
        vm.StarterCodeHtml.Should().Contain("tok-keyword");
        vm.Hints.Should().Equal("Geral", "Direta");
    }

    [Fact]
    public async Task Show_ExercicioSemCodigoInicial_NaoMostraBlocoDeCodigo()
    {
        var exercise = new ExerciseDto("Contando", "Conte os livros.", "sql", null, null, ["Use COUNT"], Points: 5);

        var result = await CreateController(exercise: exercise).Show(Path, Module, FullSlug, CancellationToken.None);

        var vm = ((ViewResult)result).Model.Should().BeOfType<LessonPageViewModel>().Subject.Exercise!;
        vm.StarterCodeHtml.Should().BeNull();
        vm.DatasetSlug.Should().BeNull();
        vm.DatasetDisplayName.Should().BeNull();
        vm.PointsLabel.Should().Be("5 pontos");
        vm.Hints.Should().Equal("Use COUNT");
    }

    [Theory]
    [InlineData(DifficultyLevel.Beginner, "Iniciante")]
    [InlineData(DifficultyLevel.Intermediate, "Intermediário")]
    [InlineData(DifficultyLevel.Advanced, "Avançado")]
    public void LevelLabels_EmPortugues(DifficultyLevel level, string expected) => LevelLabels.For(level).Should().Be(expected);

    private sealed class ThrowingService : ILessonContentService
    {
        public bool Called { get; private set; }

        public Task<LessonLoadResult> GetLessonAsync(string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default)
        {
            Called = true;
            throw new InvalidOperationException("banco fora do ar");
        }
    }

    private sealed class FixedService(LessonLoadResult result) : ILessonContentService
    {
        public Task<LessonLoadResult> GetLessonAsync(string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "DevPath.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ListLogger : ILogger<LessonsController>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
