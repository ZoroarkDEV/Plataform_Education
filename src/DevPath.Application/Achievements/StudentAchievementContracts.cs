namespace DevPath.Application.Achievements;

/// <summary>Um selo que o aluno ganhou (#45, US-BADGE.1).</summary>
/// <param name="Title">Título do selo (ex.: "Primeiro SELECT").</param>
/// <param name="Description">Frase do selo (ex.: "Você fez um banco de dados responder à sua primeira pergunta.").</param>
/// <param name="EarnedAt">Quando foi ganho (UTC, offset zero). No backfill silencioso, a data do primeiro acerto.</param>
public sealed record EarnedAchievementDto(string Title, string Description, DateTimeOffset EarnedAt);

/// <summary>
/// Selos do aluno para o dashboard ("Seus selos"). A concessão no acerto acontece no Worker, na mesma transação da
/// <c>CodeSubmission</c>, e chega à tela por <c>ExecutionOutcome.NewlyUnlockedAchievement</c>.
/// </summary>
public interface IStudentAchievementService
{
    /// <summary>
    /// Primeiro faz o backfill SILENCIOSO (concede os selos a que o aluno já tem direito e ainda não tem: quem já
    /// acertou um exercício, ou concluiu <c>seu-primeiro-select</c> com <c>ExercisePassed</c>, antes do #45) e depois
    /// devolve os selos do aluno, do MAIS ANTIGO para o mais novo (<c>EarnedAt</c> crescente; empate por título).
    /// O backfill nunca vira aviso de "desbloqueado". Lista vazia = esconder a área (nada de "0 selos").
    /// Aluno inexistente devolve lista vazia.
    /// </summary>
    Task<IReadOnlyList<EarnedAchievementDto>> ListAsync(Guid userId, CancellationToken ct = default);
}
