using DevPath.Application.Journey;
using DevPath.Domain.Progress;
using FluentAssertions;

namespace DevPath.UnitTests.Application;

/// <summary>Regras R1 (desbloqueio), R3 (percentual) e R4 (rascunho) do dashboard, sem banco (SPEC §9.1).</summary>
public class JourneyCalculatorTests
{
    private const string Path = "sql-dados";

    private static readonly Dictionary<string, Guid> Ids = new();

    private static JourneyLessonInput L(string slug, bool draft = false)
    {
        if (!Ids.TryGetValue(slug, out var id))
        {
            id = Guid.NewGuid();
            Ids[slug] = id;
        }

        return new JourneyLessonInput(id, slug, "Aula " + slug, draft);
    }

    private static JourneyModuleInput M(string slug, params JourneyLessonInput[] lessons) => new(slug, "Módulo " + slug, lessons);

    private static Dictionary<Guid, ProgressStatus> P(params (string Slug, ProgressStatus Status)[] items) =>
        items.ToDictionary(i => Ids[i.Slug], i => i.Status);

    private static DashboardDto Build(IReadOnlyList<JourneyModuleInput> modules, Dictionary<Guid, ProgressStatus>? progress = null) =>
        JourneyCalculator.Build(Path, "SQL & Dados", modules, progress ?? []);

    private static DashboardLessonDto Lesson(DashboardDto d, string slug) =>
        d.Modules.SelectMany(m => m.Lessons).Single(l => l.Lesson.LessonSlug == slug);

    // Módulo a: a1, a2(rascunho), a3 · módulo b: b1, b2 · módulo c (só rascunho): c1 · módulo d: d1
    private static readonly JourneyModuleInput[] Roadmap =
    [
        M("a", L("a1"), L("a2", draft: true), L("a3")),
        M("b", L("b1"), L("b2")),
        M("c", L("c1", draft: true)),
        M("d", L("d1")),
    ];

    [Fact]
    public void PrimeiroAcesso_PrimeiraAulaLiberada_RestanteBloqueadoPelaAnterior()
    {
        var d = Build(Roadmap);

        d.IsFirstAccess.Should().BeTrue();
        d.NextLesson.Should().Be(new LessonRefDto(Path, "a", "a1", "Aula a1"));
        Lesson(d, "a1").Status.Should().Be(JourneyLessonStatus.Available);
        Lesson(d, "a1").UnlockRequirement.Should().BeNull();

        // R4: o rascunho a2 é pulado; a3 depende de a1.
        Lesson(d, "a2").Status.Should().Be(JourneyLessonStatus.ComingSoon);
        Lesson(d, "a2").UnlockRequirement.Should().BeNull();
        Lesson(d, "a3").Status.Should().Be(JourneyLessonStatus.Locked);
        Lesson(d, "a3").UnlockRequirement!.LessonSlug.Should().Be("a1");

        // R1: a 1ª aula do módulo b depende da última aula disponível do módulo a.
        Lesson(d, "b1").UnlockRequirement.Should().Be(new LessonRefDto(Path, "a", "a3", "Aula a3"));
        Lesson(d, "b2").UnlockRequirement!.LessonSlug.Should().Be("b1");

        // Módulo só com rascunho não segura o próximo: d1 depende do módulo b.
        Lesson(d, "d1").UnlockRequirement!.LessonSlug.Should().Be("b2");
    }

    [Fact]
    public void Percentual_IgnoraRascunhos_E_TruncaParaInteiro()
    {
        var d = Build(Roadmap, P(("a1", ProgressStatus.Completed)));

        d.AvailableLessons.Should().Be(5);   // a1 a3 b1 b2 d1
        d.TotalLessons.Should().Be(7);       // + a2 c1 ("Aula X de Y")
        d.CompletedLessons.Should().Be(1);
        d.ProgressPercentage.Should().Be(20);

        var a = d.Modules.Single(m => m.Slug == "a");
        a.AvailableLessons.Should().Be(2);
        a.TotalLessons.Should().Be(3);
        a.ProgressPercentage.Should().Be(50);

        var c = d.Modules.Single(m => m.Slug == "c");
        c.AvailableLessons.Should().Be(0);
        c.ProgressPercentage.Should().Be(0);
    }

    [Theory]
    [InlineData(3, 20, 15)]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 66)]
    [InlineData(3, 3, 100)]
    [InlineData(0, 0, 0)]
    [InlineData(5, 0, 0)]
    public void Percentage_R3(int completed, int available, int expected) =>
        JourneyCalculator.Percentage(completed, available).Should().Be(expected);

    [Fact]
    public void ConcluirAula_LiberaAProxima_EmAndamentoViraContinuar()
    {
        var d = Build(Roadmap, P(("a1", ProgressStatus.Completed), ("a3", ProgressStatus.InProgress)));

        d.IsFirstAccess.Should().BeFalse();
        Lesson(d, "a1").Status.Should().Be(JourneyLessonStatus.Completed);
        Lesson(d, "a3").Status.Should().Be(JourneyLessonStatus.InProgress);
        Lesson(d, "a3").UnlockRequirement.Should().BeNull();
        d.NextLesson!.LessonSlug.Should().Be("a3");
        Lesson(d, "b1").UnlockRequirement!.LessonSlug.Should().Be("a3");
    }

    [Fact]
    public void ModuloConcluido_LiberaOProximo()
    {
        var d = Build(Roadmap, P(("a1", ProgressStatus.Completed), ("a3", ProgressStatus.Mastered)));

        Lesson(d, "b1").Status.Should().Be(JourneyLessonStatus.Available);
        Lesson(d, "b2").Status.Should().Be(JourneyLessonStatus.Locked);
        d.NextLesson!.LessonSlug.Should().Be("b1");
    }

    [Fact]
    public void ModuloSoComRascunho_NaoBloqueia()
    {
        var d = Build(Roadmap, P(("a1", ProgressStatus.Completed), ("a3", ProgressStatus.Completed),
            ("b1", ProgressStatus.Completed), ("b2", ProgressStatus.Completed)));

        Lesson(d, "c1").Status.Should().Be(JourneyLessonStatus.ComingSoon);
        Lesson(d, "d1").Status.Should().Be(JourneyLessonStatus.Available);
        d.NextLesson!.LessonSlug.Should().Be("d1");
    }

    [Fact]
    public void TudoConcluido_SemProximaAula()
    {
        var all = new[] { "a1", "a3", "b1", "b2", "d1" }.Select(s => (s, ProgressStatus.Completed)).ToArray();
        var d = Build(Roadmap, P(all));

        d.NextLesson.Should().BeNull();
        d.ProgressPercentage.Should().Be(100);
        d.AvailableLessons.Should().BeGreaterThan(0); // AllDone, não Empty
    }

    [Fact]
    public void AulaNovaInseridaAntes_NaoBloqueiaNemApagaAConcluida_R5()
    {
        // "novo" entrou antes de a1, que já estava concluída.
        var modules = new[] { M("a", L("novo"), L("a1"), L("a3")), M("b", L("b1")) };
        var d = Build(modules, P(("a1", ProgressStatus.Completed)));

        Lesson(d, "novo").Status.Should().Be(JourneyLessonStatus.Available);
        Lesson(d, "a1").Status.Should().Be(JourneyLessonStatus.Completed);
        Lesson(d, "a3").Status.Should().Be(JourneyLessonStatus.Available); // liberada por a1
        d.NextLesson!.LessonSlug.Should().Be("novo");

        // O módulo b exige o módulo a inteiro; falta "novo" (a última ainda não concluída de a é a3).
        Lesson(d, "b1").UnlockRequirement!.LessonSlug.Should().Be("a3");
        d.ProgressPercentage.Should().Be(25);
    }

    [Fact]
    public void ViradaDeModulo_RequisitoEhAUltimaNaoConcluidaDoModuloAnterior()
    {
        var modules = new[] { M("a", L("x1"), L("x2"), L("x3")), M("b", L("y1")) };
        var d = Build(modules, P(("x2", ProgressStatus.Completed), ("x3", ProgressStatus.Completed)));

        Lesson(d, "y1").UnlockRequirement!.LessonSlug.Should().Be("x1");
    }

    [Fact]
    public void RascunhoConcluido_NaoContaNoPercentual()
    {
        var d = Build(Roadmap, P(("a2", ProgressStatus.Completed)));

        Lesson(d, "a2").Status.Should().Be(JourneyLessonStatus.ComingSoon);
        d.CompletedLessons.Should().Be(0);
        d.ProgressPercentage.Should().Be(0);
    }

    [Fact]
    public void TrilhaSoComRascunhos_Vazia_SemProximaAula()
    {
        var d = Build([M("a", L("r1", draft: true), L("r2", draft: true))]);

        d.AvailableLessons.Should().Be(0);
        d.ProgressPercentage.Should().Be(0);
        d.NextLesson.Should().BeNull();
        d.IsFirstAccess.Should().BeTrue();
        d.Modules.Should().ContainSingle().Which.Lessons.Should().OnlyContain(l => l.Status == JourneyLessonStatus.ComingSoon);
    }

    [Fact]
    public void ModuloSemAulas_FicaFora_ENotStartedNaoContaComoAcesso()
    {
        var d = Build([M("vazio"), M("a", L("s1"))], P(("s1", ProgressStatus.NotStarted)));

        d.Modules.Select(m => m.Slug).Should().Equal("a");
        d.IsFirstAccess.Should().BeTrue();
        Lesson(d, "s1").Status.Should().Be(JourneyLessonStatus.Available);
    }
}
