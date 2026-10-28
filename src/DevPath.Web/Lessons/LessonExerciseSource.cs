namespace DevPath.Web.Lessons;

/// <summary>
/// Visão SEGURA do exercício de uma aula (o que o aluno pode ver), a partir de
/// <c>NN-aula.exercise.json</c> (content/README.md, seção "Exercício").
/// Fica de fora, de propósito: <c>solution</c> (protegida), as linhas esperadas de <c>test_cases</c>
/// e <c>common_errors</c> (só no servidor, para a correção).
/// </summary>
/// <remarks>
/// TEMPORÁRIO na Web: é o formato que pedimos ao Backend como <c>ExerciseDto</c> em
/// <c>LessonDto.Exercise</c> (Application/Lessons). Quando o DTO trouxer o exercício, este tipo e
/// <see cref="ILessonExerciseSource"/> saem e o controller lê direto do <c>LessonDto</c>.
/// </remarks>
/// <param name="StatementMarkdown">Enunciado em Markdown.</param>
/// <param name="Language">Linguagem do exercício ("sql", "csharp"...).</param>
/// <param name="Dataset">Nome do dataset usado (ex.: "livraria"), se houver.</param>
/// <param name="Points">Pontos (padrão 10, provisório).</param>
/// <param name="StarterCode">Código inicial, se houver. Mostrado só para leitura até o editor (#14).</param>
/// <param name="Hints">Dicas, da mais geral para a mais direta.</param>
public sealed record StudentExercise(
    string Title,
    string StatementMarkdown,
    string Language,
    string? Dataset,
    int Points,
    string? StarterCode,
    IReadOnlyList<string> Hints);

/// <summary>De onde a tela tira o exercício da aula. Ver <see cref="StudentExercise"/>.</summary>
public interface ILessonExerciseSource
{
    Task<StudentExercise?> GetForLessonAsync(Guid lessonId, CancellationToken ct);
}

/// <summary>
/// Padrão enquanto o <c>LessonDto</c> do Backend não expõe exercício: nenhuma aula tem exercício
/// e a seção some da tela. Não é um fake de conteúdo: só "ainda não temos o dado".
/// </summary>
public sealed class NoLessonExerciseSource : ILessonExerciseSource
{
    public Task<StudentExercise?> GetForLessonAsync(Guid lessonId, CancellationToken ct) =>
        Task.FromResult<StudentExercise?>(null);
}
