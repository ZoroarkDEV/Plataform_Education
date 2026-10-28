using DevPath.Application.Lessons;
using DevPath.Domain.Common;
using DevPath.Web.Lessons;

namespace DevPath.Web.Models.Lessons;

/// <summary>Tudo que a tela da aula precisa: dados da aula + HTML já convertido e seguro.</summary>
/// <param name="Exercise">Exercício da aula, ou null (a seção não aparece).</param>
public sealed record LessonPageViewModel(LessonDto Lesson, RenderedLesson Html, LessonExerciseViewModel? Exercise = null)
{
    public string LevelLabel => LevelLabels.For(Lesson.Level);

    public string EstimatedTimeLabel => $"~{Lesson.EstimatedMinutes} min";

    /// <summary>"Aula X de Y" (ou só "Aula" se o serviço não informou a posição).</summary>
    public string PositionLabel => Lesson is { Position: > 0, TotalInModule: > 0 }
        ? $"Aula {Lesson.Position} de {Lesson.TotalInModule}"
        : "Aula";
}

/// <summary>
/// Exercício pronto para a tela: só o que o aluno pode ver (sem solução, sem linhas esperadas,
/// sem erros comuns). Enunciado e código inicial já convertidos em HTML seguro.
/// </summary>
public sealed record LessonExerciseViewModel(
    string Title,
    string StatementHtml,
    string LanguageLabel,
    string? Dataset,
    int Points,
    string? StarterCodeHtml,
    IReadOnlyList<string> Hints)
{
    public static LessonExerciseViewModel From(ExerciseDto exercise, LessonMarkdownRenderer renderer) => new(
        exercise.Title,
        renderer.RenderFragment(exercise.StatementMarkdown),
        CodeHighlighter.LabelFor(exercise.Language) ?? "Código",
        string.IsNullOrWhiteSpace(exercise.Dataset) ? null : exercise.Dataset,
        exercise.Points,
        string.IsNullOrWhiteSpace(exercise.StarterCode) ? null : LessonMarkdownRenderer.RenderCodeBlock(exercise.StarterCode, exercise.Language),
        exercise.Hints.Where(h => !string.IsNullOrWhiteSpace(h)).ToList());

    public string PointsLabel => Points == 1 ? "1 ponto" : $"{Points} pontos";
}

public enum LessonUnavailableReason
{
    NotFound,
    Malformed,
    Draft,
}

/// <summary>Página amigável quando a aula não abre (R6: o que aconteceu + o que fazer agora).</summary>
/// <param name="TechnicalDetail">Só preenchido em Development; aparece recolhido em "Ver detalhe técnico".</param>
public sealed record LessonUnavailableViewModel(LessonUnavailableReason Reason, string? TechnicalDetail)
{
    /// <summary>Decisão de produto: rascunho aberto por link direto = 404 com este texto.</summary>
    public const string DraftMessage = "Esta aula está sendo escrita. Volte ao dashboard e siga pelas aulas já disponíveis.";

    public const string MalformedMessage = "Não conseguimos carregar esta aula agora. Tente de novo ou volte ao dashboard.";

    public string Title => Reason switch
    {
        LessonUnavailableReason.Draft => "Aula em breve",
        LessonUnavailableReason.NotFound => "Aula não encontrada",
        _ => "Aula indisponível",
    };

    public string Message => Reason switch
    {
        LessonUnavailableReason.Draft => DraftMessage,
        LessonUnavailableReason.NotFound => "Não encontramos esta aula. Confira o endereço ou volte ao dashboard para escolher uma aula.",
        _ => MalformedMessage,
    };
}

/// <summary>Nível de dificuldade em português, para o aluno.</summary>
public static class LevelLabels
{
    public static string For(DifficultyLevel level) => level switch
    {
        DifficultyLevel.Beginner => "Iniciante",
        DifficultyLevel.Intermediate => "Intermediário",
        DifficultyLevel.Advanced => "Avançado",
        _ => "Nível não informado",
    };
}
