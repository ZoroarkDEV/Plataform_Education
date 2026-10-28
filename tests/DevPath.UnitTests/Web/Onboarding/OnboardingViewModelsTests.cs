using DevPath.Domain.Common;
using DevPath.Web.ViewModels.Onboarding;
using FluentAssertions;

namespace DevPath.UnitTests.Web.Onboarding;

/// <summary>Textos e estados das telas do #11 (US-11.2, US-11.3, R6, R7).</summary>
public class OnboardingViewModelsTests
{
    private static readonly LessonRef Select = new("sql-dados", "primeiros-passos", "seu-primeiro-select", "Seu primeiro SELECT");

    [Theory]
    [InlineData(15, 3, 20, "15% concluído — 3 de 20 aulas")]
    [InlineData(0, 0, 1, "0% concluído — 0 de 1 aula")]
    [InlineData(100, 20, 20, "100% concluído — 20 de 20 aulas")]
    [InlineData(140, 3, 2, "100% concluído — 3 de 2 aulas")] // nunca passa de 100%
    [InlineData(-5, 0, 2, "0% concluído — 0 de 2 aulas")]
    public void Progresso_TextoNoFormatoDoPO(int percentage, int completed, int available, string expected)
    {
        Labels.Progress(percentage, completed, available).Should().Be(expected);
    }

    [Theory]
    [InlineData(LessonStatus.Completed, "Concluída")]
    [InlineData(LessonStatus.InProgress, "Em andamento")]
    [InlineData(LessonStatus.Available, "Liberada")]
    [InlineData(LessonStatus.Locked, "Bloqueada")]
    [InlineData(LessonStatus.ComingSoon, "Em breve")]
    public void Status_TemTextoEmPortugues(LessonStatus status, string expected)
    {
        new LessonItemViewModel(Select, status).StatusLabel.Should().Be(expected);
    }

    [Theory]
    [InlineData(LessonStatus.Completed, true)]
    [InlineData(LessonStatus.InProgress, true)]
    [InlineData(LessonStatus.Available, true)]
    [InlineData(LessonStatus.Locked, false)]
    [InlineData(LessonStatus.ComingSoon, false)]
    public void SoAulaLiberadaAbre(LessonStatus status, bool canOpen)
    {
        new LessonItemViewModel(Select, status).CanOpen.Should().Be(canOpen);
    }

    [Fact]
    public void LessonRef_MontaUrlDaTelaDeAulaComSlugsEscapados()
    {
        Select.Url.Should().Be("/trilhas/sql-dados/primeiros-passos/seu-primeiro-select");
        new LessonRef("a b", "c/d", "e?f", "X").Url.Should().Be("/trilhas/a%20b/c%2Fd/e%3Ff");
    }

    [Fact]
    public void Dashboard_SemAulasDisponiveis_EhEstadoVazio()
    {
        Dashboard(available: 0, next: null).State.Should().Be(DashboardState.Empty);
    }

    [Fact]
    public void Dashboard_ComProximaAula_EhContinuar()
    {
        Dashboard(available: 20, next: Select).State.Should().Be(DashboardState.Continue);
    }

    [Fact]
    public void Dashboard_SemProximaAula_EhTudoConcluido()
    {
        Dashboard(available: 3, next: null).State.Should().Be(DashboardState.AllDone);
    }

    [Theory]
    [InlineData(true, "Começar pela aula 1")]
    [InlineData(false, "Continuar")]
    public void Dashboard_TextoDoBotao(bool firstAccess, string expected)
    {
        Dashboard(available: 20, next: Select, firstAccess: firstAccess).ContinueLabel.Should().Be(expected);
    }

    [Fact]
    public void Modulo_SemAulasDisponiveis_MostraEmBreve()
    {
        new ModuleProgressViewModel("ambiente", "Ambiente", 0, 0, 0, []).ProgressLabel.Should().Be("Em breve");
        new ModuleProgressViewModel("crud", "CRUD", 2, 5, 40, []).ProgressLabel.Should().Be("40% concluído — 2 de 5 aulas");
    }

    [Theory]
    [InlineData(DifficultyLevel.Beginner, "Para iniciantes")]
    [InlineData(DifficultyLevel.Intermediate, "Nível intermediário")]
    [InlineData(DifficultyLevel.Advanced, "Nível avançado")]
    public void Trilha_Nivel(DifficultyLevel level, string expected)
    {
        Path(level: level).LevelLabel.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "Aulas em breve")]
    [InlineData(1, "1 aula disponível")]
    [InlineData(20, "20 aulas disponíveis")]
    public void Trilha_AulasDisponiveis(int lessons, string expected)
    {
        Path(lessons: lessons).LessonsLabel.Should().Be(expected);
    }

    [Theory]
    [InlineData(40, "Cerca de 40 h")]
    [InlineData(0, "Duração a definir")]
    public void Trilha_Horas(int hours, string expected)
    {
        Path(hours: hours).HoursLabel.Should().Be(expected);
    }

    private static DashboardViewModel Dashboard(int available, LessonRef? next, bool firstAccess = false) =>
        new("sql-dados", "SQL & Dados", 0, available, 0, next, firstAccess, []);

    private static PathOptionViewModel Path(DifficultyLevel level = DifficultyLevel.Beginner, int hours = 40, int lessons = 20) =>
        new("sql-dados", "SQL & Dados", "Descrição.", level, hours, lessons);
}
