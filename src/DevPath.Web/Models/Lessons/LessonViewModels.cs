using DevPath.Application.Journey;
using DevPath.Application.Lessons;
using DevPath.Application.Progress;
using DevPath.Domain.Common;
using DevPath.Web.Lessons;

namespace DevPath.Web.Models.Lessons;

/// <summary>Tudo que a tela da aula precisa: dados da aula + HTML já convertido e seguro.</summary>
/// <param name="Exercise">Exercício da aula, ou null (a seção não aparece).</param>
/// <param name="Progress">
/// Estado do aluno nesta aula (#27, ADR-007): status, bloco de conclusão, exercício (tentativas, "Seguir sem pontos")
/// e as vizinhas na ordem da trilha (Previous/Next, com Locked e UnlockRequirement). null só em testes antigos.
/// </param>
public sealed record LessonPageViewModel(
    LessonDto Lesson,
    RenderedLesson Html,
    LessonExerciseViewModel? Exercise = null,
    LessonProgressState? Progress = null)
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
    IReadOnlyList<string> Hints,
    string? DatasetDisplayName = null,
    Guid? ExerciseId = null)
{
    public static LessonExerciseViewModel From(ExerciseDto exercise, LessonMarkdownRenderer renderer) => new(
        exercise.Title,
        renderer.RenderFragment(exercise.StatementMarkdown, LessonMarkdownRenderer.StatementMinHeadingLevel),
        CodeHighlighter.LabelFor(exercise.Language) ?? "Código",
        string.IsNullOrWhiteSpace(exercise.Dataset) ? null : exercise.Dataset,
        exercise.Points,
        string.IsNullOrWhiteSpace(exercise.StarterCode) ? null : LessonMarkdownRenderer.RenderCodeBlock(exercise.StarterCode, exercise.Language),
        exercise.Hints.Where(h => !string.IsNullOrWhiteSpace(h)).ToList(),
        exercise.DatasetDisplayName ?? DatasetDisplayNames.Resolve(exercise.Dataset),
        exercise.Id == Guid.Empty ? null : exercise.Id);

    /// <summary>Nome legível do dataset ("Livraria"); cai no slug se DisplayName vier vazio.</summary>
    public string? DatasetLabel => DatasetDisplayName ?? Dataset;

    public string PointsLabel => Points == 1 ? "1 ponto" : $"{Points} pontos";
}

public enum LessonUnavailableReason
{
    NotFound,
    Malformed,
    Draft,

    /// <summary>Aula da trilha do aluno, ainda bloqueada (R1). HTTP 403.</summary>
    Locked,

    /// <summary>Aula de outra trilha (o aluno está matriculado em outra). HTTP 403.</summary>
    OtherPath,
}

/// <summary>Página amigável quando a aula não abre (R6: o que aconteceu + o que fazer agora).</summary>
/// <param name="TechnicalDetail">Só preenchido em Development; aparece recolhido em "Ver detalhe técnico".</param>
/// <param name="UnlockRequirement">Só em Locked: a aula que falta concluir (o mesmo do dashboard).</param>
/// <param name="CurrentPathTitle">Só em OtherPath: a trilha atual do aluno.</param>
/// <remarks>Textos de Locked/OtherPath são provisórios do Backend (#27): o UI Operator refina a página.</remarks>
public sealed record LessonUnavailableViewModel(
    LessonUnavailableReason Reason,
    string? TechnicalDetail,
    LessonRefDto? UnlockRequirement = null,
    string? CurrentPathTitle = null)
{
    /// <summary>Decisão de produto: rascunho aberto por link direto = 404 com este texto.</summary>
    public const string DraftMessage = "Esta aula está sendo escrita. Volte ao dashboard e siga pelas aulas já disponíveis.";

    public const string MalformedMessage = "Não conseguimos carregar esta aula agora. Tente de novo ou volte ao dashboard.";

    public string Title => Reason switch
    {
        LessonUnavailableReason.Draft => "Aula em breve",
        LessonUnavailableReason.NotFound => "Aula não encontrada",
        LessonUnavailableReason.Locked => "Aula bloqueada",
        LessonUnavailableReason.OtherPath => "Aula de outra trilha",
        _ => "Aula indisponível",
    };

    public string Message => Reason switch
    {
        LessonUnavailableReason.Draft => DraftMessage,
        LessonUnavailableReason.NotFound => "Não encontramos esta aula. Confira o endereço ou volte ao dashboard para escolher uma aula.",
        LessonUnavailableReason.Locked => UnlockRequirement is { } r
            ? $"Conclua \"{r.Title}\" para liberar esta aula."
            : "Conclua as aulas anteriores para liberar esta aula.",
        LessonUnavailableReason.OtherPath => CurrentPathTitle is { Length: > 0 } t
            ? $"Esta aula não faz parte da sua trilha atual ({t}). Volte ao dashboard para continuar."
            : "Esta aula não faz parte da sua trilha atual. Volte ao dashboard para continuar.",
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
