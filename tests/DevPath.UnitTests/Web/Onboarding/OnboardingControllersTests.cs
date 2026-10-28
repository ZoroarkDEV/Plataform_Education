using System.Security.Claims;
using DevPath.Application.Auth;
using DevPath.Domain.Common;
using DevPath.Web.Controllers;
using DevPath.Web.Onboarding;
using DevPath.Web.ViewModels.Account;
using DevPath.Web.ViewModels.Onboarding;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevPath.UnitTests.Web.Onboarding;

/// <summary>Decisões dos controllers do #11, com um dublê da porta <see cref="IStudentJourneyGateway"/>.</summary>
public class OnboardingControllersTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly PathOptionViewModel Sql =
        new("sql-dados", "SQL & Dados", "SQL do zero.", DifficultyLevel.Beginner, 40, 20);

    [Fact]
    public async Task Escolha_JaTemTrilha_RedirecionaParaODashboard()
    {
        var controller = Onboarding(new StubGateway { HasPath = true });

        var result = await controller.ChoosePath(CancellationToken.None);

        result.Should().BeOfType<RedirectToActionResult>().Which.ControllerName.Should().Be("Dashboard");
    }

    [Fact]
    public async Task Escolha_SemTrilha_MostraAsTrilhasAtivas()
    {
        var controller = Onboarding(new StubGateway { Paths = [Sql] });

        var result = await controller.ChoosePath(CancellationToken.None);

        var model = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ChoosePathViewModel>().Subject;
        model.Paths.Should().ContainSingle().Which.Slug.Should().Be("sql-dados");
        model.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Escolha_Matriculou_GuardaConfirmacaoEVaiParaODashboard()
    {
        var controller = Onboarding(new StubGateway { Outcome = EnrollmentOutcome.Enrolled("SQL & Dados") });

        var result = await controller.ChoosePath("sql-dados", CancellationToken.None);

        result.Should().BeOfType<RedirectToActionResult>().Which.ControllerName.Should().Be("Dashboard");
        controller.TempData[OnboardingController.FlashKey].Should().Be("Você começou a trilha SQL & Dados!");
    }

    [Theory]
    [InlineData(EnrollmentStatus.PathNotFound, "Não encontramos essa trilha.")]
    [InlineData(EnrollmentStatus.Unavailable, "Não conseguimos salvar sua escolha agora.")]
    public async Task Escolha_Falhou_VoltaParaAEscolhaComMensagem(EnrollmentStatus status, string message)
    {
        var controller = Onboarding(new StubGateway { Outcome = new EnrollmentOutcome(status) });

        var result = await controller.ChoosePath("sql-dados", CancellationToken.None);

        result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be(nameof(OnboardingController.ChoosePath));
        controller.TempData.Values.OfType<string>().Should().Contain(v => v.StartsWith(message));
    }

    [Fact]
    public async Task Escolha_SemSlug_NaoChamaOServico()
    {
        var gateway = new StubGateway();
        var controller = Onboarding(gateway);

        await controller.ChoosePath("  ", CancellationToken.None);

        gateway.EnrollCalls.Should().Be(0);
    }

    [Fact]
    public async Task Escolha_SemIdDeUsuario_PedeLogin()
    {
        var controller = Onboarding(new StubGateway(), withUser: false);

        (await controller.ChoosePath(CancellationToken.None)).Should().BeOfType<ChallengeResult>();
    }

    [Fact]
    public async Task Dashboard_SemTrilha_RedirecionaParaAEscolha()
    {
        var controller = WithContext(new DashboardController(new StubGateway()));

        var result = await controller.Index(CancellationToken.None);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ControllerName.Should().Be("Onboarding");
        redirect.ActionName.Should().Be(nameof(OnboardingController.ChoosePath));
    }

    [Fact]
    public async Task Dashboard_ComTrilha_MostraODashboardEAConfirmacao()
    {
        var dashboard = new DashboardViewModel("sql-dados", "SQL & Dados", 0, 20, 0, null, true, []);
        var controller = WithContext(new DashboardController(new StubGateway { Dashboard = dashboard }));
        controller.TempData[OnboardingController.FlashKey] = "Você começou a trilha SQL & Dados!";

        var result = await controller.Index(CancellationToken.None);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.Model.Should().BeSameAs(dashboard);
        view.ViewData[OnboardingController.FlashKey].Should().Be("Você começou a trilha SQL & Dados!");
        view.ViewData["DisplayName"].Should().Be("Ana");
    }

    [Fact]
    public async Task Cadastro_ComSucesso_VaiParaEscolhaDeTrilha()
    {
        var controller = WithContext(new AccountController(new SucceedingAuth()));

        var result = await controller.Register(new RegisterViewModel
        {
            DisplayName = "Ana", Email = "ana@devpath.test", Password = "Senha123", ConfirmPassword = "Senha123"
        }, CancellationToken.None);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ControllerName.Should().Be("Onboarding");
        redirect.ActionName.Should().Be(nameof(OnboardingController.ChoosePath));
    }

    [Fact]
    public async Task ServicoPendente_DevolveEstadosVaziosSemExcecao()
    {
        var pending = new PendingStudentJourneyGateway(NullLogger<PendingStudentJourneyGateway>.Instance);

        (await pending.GetActivePathsAsync(CancellationToken.None)).Should().BeEmpty();
        (await pending.HasCurrentPathAsync(UserId, CancellationToken.None)).Should().BeFalse();
        (await pending.GetDashboardAsync(UserId, CancellationToken.None)).Should().BeNull();
        (await pending.EnrollAsync(UserId, "sql-dados", CancellationToken.None)).Status.Should().Be(EnrollmentStatus.Unavailable);
    }

    private static OnboardingController Onboarding(StubGateway gateway, bool withUser = true) =>
        WithContext(new OnboardingController(gateway), withUser);

    private static T WithContext<T>(T controller, bool withUser = true) where T : Controller
    {
        var claims = withUser
            ? new[] { new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(DevPathClaimTypes.DisplayName, "Ana") }
            : [];
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider());
        return controller;
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> _values = new Dictionary<string, object>();
        public IDictionary<string, object> LoadTempData(HttpContext context) => _values;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _values = values;
    }

    private sealed class StubGateway : IStudentJourneyGateway
    {
        public IReadOnlyList<PathOptionViewModel> Paths { get; init; } = [];
        public bool HasPath { get; init; }
        public EnrollmentOutcome Outcome { get; init; } = EnrollmentOutcome.Unavailable();
        public DashboardViewModel? Dashboard { get; init; }
        public int EnrollCalls { get; private set; }

        public Task<IReadOnlyList<PathOptionViewModel>> GetActivePathsAsync(CancellationToken cancellationToken) => Task.FromResult(Paths);
        public Task<bool> HasCurrentPathAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(HasPath);

        public Task<EnrollmentOutcome> EnrollAsync(Guid userId, string pathSlug, CancellationToken cancellationToken)
        {
            EnrollCalls++;
            return Task.FromResult(Outcome);
        }

        public Task<DashboardViewModel?> GetDashboardAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Dashboard);
    }

    private sealed class SucceedingAuth : IAuthService
    {
        public Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) => Task.FromResult(AuthResult.Success());
        public Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) => Task.FromResult(AuthResult.Success());
        public Task LogoutAsync() => Task.CompletedTask;
    }
}
