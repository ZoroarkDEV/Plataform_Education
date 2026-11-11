namespace DevPath.Web.Progress;

/// <summary>
/// Selo ganho pelo aluno (#45). Mesmo formato do <c>EarnedAchievementDto</c> do Backend
/// (<c>Title</c>, <c>Description</c>, <c>EarnedAt</c> em UTC): o adapter será um wrap fino.
/// </summary>
public sealed record EarnedAchievement(string Title, string Description, DateTimeOffset EarnedAt);

/// <summary>
/// Porta da Web para os selos do aluno (#45). <see cref="ListAsync"/> alimenta "Seus selos" no
/// dashboard (o serviço do Backend já faz o backfill silencioso de quem acertou a aula 1 antes).
/// <see cref="GetNewlyUnlockedAsync"/> é o gancho do aviso na aula: o adapter vai preencher a
/// partir de <c>ExecutionOutcome.NewlyUnlockedAchievement</c> (só no pass que acaba de conceder;
/// null em refazer, "Seguir sem pontos" e tentativas esgotadas). Enquanto o Backend não entrega
/// o serviço, a UI usa <see cref="EmptyStudentAchievementSource"/>.
/// </summary>
public interface IStudentAchievementSource
{
    Task<IReadOnlyList<EarnedAchievement>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Selo recém-desbloqueado neste pass, ou null se não houve desbloqueio agora.</summary>
    Task<EarnedAchievement?> GetNewlyUnlockedAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementação vazia enquanto o <c>IStudentAchievementService</c> do Backend não está na dev.
/// Nenhum selo, nenhum aviso de desbloqueio: o app real permanece igual para os alunos.
/// </summary>
public sealed class EmptyStudentAchievementSource : IStudentAchievementSource
{
    public static readonly EmptyStudentAchievementSource Instance = new();

    public Task<IReadOnlyList<EarnedAchievement>> ListAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EarnedAchievement>>([]);

    public Task<EarnedAchievement?> GetNewlyUnlockedAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<EarnedAchievement?>(null);
}
