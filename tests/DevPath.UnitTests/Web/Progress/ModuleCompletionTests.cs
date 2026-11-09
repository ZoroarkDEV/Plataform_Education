using DevPath.Application.Progress;
using DevPath.Web.Models.Lessons;
using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;
using FluentAssertions;

namespace DevPath.UnitTests.Web.Progress;

/// <summary>Tela "Módulo concluído" (#42, US-M.1): o que entra no bloco a partir do dashboard do servidor.</summary>
public sealed class ModuleCompletionTests
{
    private const string PathSlug = "sql-dados";
    private const string M1 = "primeiros-passos-com-sql";
    private const string M2 = "mais-consultas-com-sql";
    private const string Drafts = "ambiente";

    private static LessonRef L(string module, string slug) => new(PathSlug, module, slug, $"Aula {slug}");

    private static ModuleProgressViewModel Module1(bool done, params int?[] points) => new(M1, "Primeiros passos com SQL",
        done ? 3 : 2, 3, done ? 100 : 66,
        [
            new(L(M1, "a"), LessonStatus.Completed, Points: points.ElementAtOrDefault(0)),
            new(L(M1, "b"), LessonStatus.Completed, Points: points.ElementAtOrDefault(1)),
            new(L(M1, "c"), done ? LessonStatus.Completed : LessonStatus.InProgress, Points: done ? points.ElementAtOrDefault(2) : null),
            new(L(M1, "rascunho"), LessonStatus.ComingSoon),
        ]);

    private static ModuleProgressViewModel DraftsOnly() => new(Drafts, "Ambiente", 0, 0, 0,
        [new(L(Drafts, "x"), LessonStatus.ComingSoon)]);

    private static ModuleProgressViewModel Module2(LessonStatus first = LessonStatus.Available) => new(M2, "Mais consultas com SQL",
        first == LessonStatus.Completed ? 1 : 0, 2, first == LessonStatus.Completed ? 50 : 0,
        [
            new(L(M2, "like"), first),
            new(L(M2, "distinct"), first == LessonStatus.Completed ? LessonStatus.Available : LessonStatus.Locked, L(M2, "like")),
        ]);

    private static DashboardViewModel Dashboard(LessonRef? next, int percent, params ModuleProgressViewModel[] modules) =>
        new(PathSlug, "SQL & Dados", modules.Sum(m => m.CompletedLessons), modules.Sum(m => m.AvailableLessons), percent,
            next, false, modules, modules.SelectMany(m => m.Lessons).Sum(l => l.Points ?? 0));

    [Fact]
    public void ModuloAindaComAulaProntaPendente_NaoConclui()
    {
        var dashboard = Dashboard(L(M1, "c"), 66, Module1(done: false, 10, 10));

        ModuleCompletionViewModel.TryBuild(dashboard, M1, ModuleCompletionContent.Empty).Should().BeNull();
        dashboard.Modules[0].IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void RascunhoNaoConta_ModuloSoComRascunhosNuncaConclui()
    {
        Module1(done: true).IsCompleted.Should().BeTrue("a aula em rascunho não segura o módulo (R4)");
        DraftsOnly().IsCompleted.Should().BeFalse();
        ModuleCompletionViewModel.TryBuild(Dashboard(null, 100, Module1(true), DraftsOnly()), Drafts, ModuleCompletionContent.Empty)
            .Should().BeNull();
        ModuleCompletionViewModel.TryBuild(Dashboard(null, 100, Module1(true)), "nao-existe", ModuleCompletionContent.Empty)
            .Should().BeNull();
    }

    [Fact]
    public void ComModulo2Pronto_BotaoLevaAPrimeiraAulaDele_PulandoModuloSoComRascunhos()
    {
        var dashboard = Dashboard(L(M2, "like"), 60, Module1(true, 10, 0, 10), DraftsOnly(), Module2());

        var vm = ModuleCompletionViewModel.TryBuild(dashboard, M1, ModuleCompletionContent.Empty)!;

        vm.Title.Should().Be("Módulo concluído: Primeiros passos com SQL");
        vm.ModulePoints.Should().Be(20, "soma dos pontos do servidor só das aulas deste módulo");
        vm.Footer.Should().Be("+20 pontos neste módulo · Trilha SQL & Dados: 60%");
        vm.Next.Should().NotBeNull();
        vm.Next!.Number.Should().Be(3, "a numeração é a mesma do dashboard (posição na lista)");
        vm.Next.FirstLesson.Should().Be(L(M2, "like"));
        vm.Next.ButtonLabel.Should().Be("Começar o módulo 3: Mais consultas com SQL");
        vm.SummaryUrl.Should().Be($"/trilhas/{PathSlug}/modulos/{M1}/concluido");
    }

    [Fact]
    public void ProximoModuloJaComecado_TextoIrPara_EAPrimeiraAulaNaoConcluida()
    {
        var dashboard = Dashboard(L(M2, "distinct"), 80, Module1(true), Module2(LessonStatus.Completed));

        var vm = ModuleCompletionViewModel.TryBuild(dashboard, M1, ModuleCompletionContent.Empty)!;

        vm.Next!.FirstLesson.Should().Be(L(M2, "distinct"));
        vm.Next.ButtonLabel.Should().Be("Ir para o módulo 2: Mais consultas com SQL");
    }

    [Fact]
    public void ModuloSeguinteSoEmBreve_SemBotao_TextoDoProximoChegando()
    {
        var dashboard = Dashboard(null, 100, Module1(true, 10, 10, 10), DraftsOnly());

        var vm = ModuleCompletionViewModel.TryBuild(dashboard, M1, ModuleCompletionContent.Empty)!;

        vm.Next.Should().BeNull("não há botão para rascunho");
        vm.HasLaterModule.Should().BeTrue();
        vm.NoNextText.Should().Be("O próximo módulo está chegando. Seu progresso fica guardado.");
        vm.Footer.Should().Be("+30 pontos neste módulo · Trilha SQL & Dados: 100%");
    }

    [Fact]
    public void NenhumModuloDepois_SemBotao_TextoDeTudoPronto()
    {
        var vm = ModuleCompletionViewModel.TryBuild(Dashboard(null, 100, Module1(true, 10, 10, 10)), M1, ModuleCompletionContent.Empty)!;

        vm.Next.Should().BeNull();
        vm.HasLaterModule.Should().BeFalse();
        vm.NoNextText.Should().Be(
            "Você terminou tudo o que está pronto até agora. Novas aulas estão chegando, e seu progresso fica guardado.");
    }

    [Fact]
    public void ComBotao_SemTextoDeEspera()
    {
        var vm = ModuleCompletionViewModel.TryBuild(Dashboard(L(M2, "like"), 60, Module1(true), Module2()), M1, ModuleCompletionContent.Empty)!;

        vm.Next.Should().NotBeNull();
        vm.NoNextText.Should().BeNull();
    }

    [Fact]
    public void ProximoModuloSemAulaQueAbra_SemBotao_TextoDoProximoChegando()
    {
        var locked = new ModuleProgressViewModel(M2, "Mais consultas com SQL", 0, 1, 0,
            [new(L(M2, "like"), LessonStatus.Locked, L(M1, "c"))]);

        var vm = ModuleCompletionViewModel.TryBuild(Dashboard(null, 75, Module1(true), locked), M1, ModuleCompletionContent.Empty)!;

        vm.Next.Should().BeNull();
        vm.NoNextText.Should().Be(ModuleCompletionTexts.NextModuleComingSoon);
    }

    [Fact]
    public void FrasesDoConteudo_UmaPorAulaPronta_NaOrdem_SemRascunhoNemVazias()
    {
        var content = new ModuleCompletionContent("  Subtítulo do módulo.  ", new Dictionary<string, string>
        {
            ["c"] = "Frase C.",
            ["a"] = " Frase A. ",
            ["b"] = "  ",
            ["rascunho"] = "Não aparece.",
        });

        var vm = ModuleCompletionViewModel.TryBuild(Dashboard(null, 100, Module1(true)), M1, content)!;

        vm.Learned.Should().Equal("Frase A.", "Frase C.");
        vm.Subtitle.Should().Be("Subtítulo do módulo.");
    }

    [Fact]
    public void SemTextosDoConteudo_SemListaNemSubtitulo()
    {
        var vm = ModuleCompletionViewModel.TryBuild(Dashboard(null, 100, Module1(true)), M1, ModuleCompletionContent.Empty)!;

        vm.Learned.Should().BeEmpty();
        vm.Subtitle.Should().BeNull();
    }

    [Fact]
    public void Dashboard_ProximoPasso_EhOModuloSeguinte_DepoisDeUmModuloConcluido()
    {
        Dashboard(L(M2, "like"), 60, Module1(true), DraftsOnly(), Module2()).NextModule
            .Should().Be((3, "Mais consultas com SQL"));

        Dashboard(L(M1, "c"), 50, Module1(false), Module2(LessonStatus.Locked)).NextModule
            .Should().BeNull("ainda no módulo 1");
        Dashboard(L(M2, "distinct"), 80, Module1(true), Module2(LessonStatus.Completed)).NextModule
            .Should().BeNull("o módulo 2 já começou: é só o Continuar");
        Dashboard(null, 100, Module1(true)).NextModule.Should().BeNull();
        ModuleCompletionTexts.DashboardNextModule(2, "Mais consultas com SQL")
            .Should().Be("Próximo passo: módulo 2, Mais consultas com SQL");
    }

    [Fact]
    public async Task Reader_SoDevolveConcluido_DaTrilhaAtual()
    {
        var dashboard = Dashboard(L(M2, "like"), 60, Module1(true, 10, 10, 10), Module2());
        var reader = new ModuleCompletionReader(new StubJourney(dashboard), new StubContentService(null).AsSource());

        (await reader.LookupAsync(Guid.NewGuid(), PathSlug, M1, default)).Status.Should().Be(ModuleCompletionLookupStatus.Completed);
        (await reader.LookupAsync(Guid.NewGuid(), PathSlug, M2, default)).Status.Should().Be(ModuleCompletionLookupStatus.NotCompleted);
        (await reader.LookupAsync(Guid.NewGuid(), "outra-trilha", M1, default)).Status.Should().Be(ModuleCompletionLookupStatus.NotFound);
        (await reader.LookupAsync(Guid.NewGuid(), PathSlug, "nao-existe", default)).Status.Should().Be(ModuleCompletionLookupStatus.NotFound);

        var noPath = new ModuleCompletionReader(new StubJourney(null), new StubContentService(null).AsSource());
        (await noPath.LookupAsync(Guid.NewGuid(), PathSlug, M1, default)).Status.Should().Be(ModuleCompletionLookupStatus.NoPath);
    }

    [Fact]
    public async Task Adapter_NullDoServico_ViraEmpty()
    {
        var content = await new StubContentService(null).AsSource().GetAsync(PathSlug, "nao-existe", default);

        content.Subtitle.Should().BeNull();
        content.LearnedByLessonSlug.Should().BeEmpty();
    }

    [Fact]
    public async Task Adapter_MapeiaSubtituloEFrases_NaOrdemDoServico()
    {
        var dto = new ModuleCompletionContentDto("Subtítulo.", new Dictionary<string, string>
        {
            ["c"] = "Frase C.",
            ["a"] = "Frase A.",
        });
        var service = new StubContentService(dto);

        var content = await service.AsSource().GetAsync(PathSlug, M1, default);

        service.Calls.Should().Equal((PathSlug, M1));
        content.Subtitle.Should().Be("Subtítulo.");
        content.LearnedByLessonSlug.Keys.Should().Equal("c", "a");
        content.LearnedByLessonSlug["a"].Should().Be("Frase A.");
    }

    [Fact]
    public async Task Adapter_AulaSemFrase_NaoEntraNaLista_EAOrdemFinalEhADasAulas()
    {
        // O serviço só manda as aulas com frase ("b" não tem); a tela lista na ordem das aulas do dashboard.
        var dto = new ModuleCompletionContentDto(null, new Dictionary<string, string>
        {
            ["c"] = "Frase C.",
            ["a"] = "Frase A.",
        });

        var content = await new StubContentService(dto).AsSource().GetAsync(PathSlug, M1, default);
        var vm = ModuleCompletionViewModel.TryBuild(Dashboard(null, 100, Module1(true)), M1, content)!;

        content.LearnedByLessonSlug.Should().NotContainKey("b");
        vm.Learned.Should().Equal("Frase A.", "Frase C.");
        vm.Subtitle.Should().BeNull();
    }

    private sealed class StubContentService(ModuleCompletionContentDto? dto) : IModuleCompletionContentService
    {
        public List<(string, string)> Calls { get; } = [];
        public ModuleCompletionContentSource AsSource() => new(this);
        public Task<ModuleCompletionContentDto?> GetAsync(string pathSlug, string moduleSlug, CancellationToken cancellationToken = default)
        {
            Calls.Add((pathSlug, moduleSlug));
            return Task.FromResult(dto);
        }
    }

    private sealed class StubJourney(DashboardViewModel? dashboard) : DevPath.Web.Onboarding.IStudentJourneyGateway
    {
        public Task<IReadOnlyList<PathOptionViewModel>> GetActivePathsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PathOptionViewModel>>([]);
        public Task<bool> HasCurrentPathAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(dashboard is not null);
        public Task<DevPath.Web.Onboarding.EnrollmentOutcome> EnrollAsync(Guid userId, string pathSlug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<DashboardViewModel?> GetDashboardAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(dashboard);
    }
}
