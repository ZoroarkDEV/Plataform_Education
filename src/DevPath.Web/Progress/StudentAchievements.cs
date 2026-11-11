using System.Collections.Concurrent;
using DevPath.Application.Achievements;

namespace DevPath.Web.Progress;

/// <summary>
/// Selo ganho pelo aluno (#45). Mesmo formato do <see cref="EarnedAchievementDto"/> do Backend
/// (<c>Title</c>, <c>Description</c>, <c>EarnedAt</c> em UTC).
/// </summary>
public sealed record EarnedAchievement(string Title, string Description, DateTimeOffset EarnedAt);

/// <summary>
/// Porta da Web para os selos do aluno (#45). <see cref="ListAsync"/> alimenta "Seus selos" no
/// dashboard (o serviço do Backend já faz o backfill silencioso e devolve do mais antigo para o mais novo;
/// <c>EarnedAt</c> é a data do primeiro acerto, não a do backfill).
/// </summary>
/// <remarks>
/// O aviso "Selo desbloqueado" NÃO vem daqui: só <c>ExecutionOutcome.NewlyUnlockedAchievement</c>
/// (JSON <c>newlyUnlockedAchievement</c>) no pass que acabou de gravar o selo. O adapter devolve
/// sempre null em <see cref="GetNewlyUnlockedAsync"/>; o endpoint de Executar guarda o DTO num
/// buffer de uma leitura, e o GET /conclusao consome. Um GET seguinte não mostra o aviso de novo.
/// Dublês de teste podem preencher o gancho para montar a partial sem passar pelo Executar.
/// </remarks>
public interface IStudentAchievementSource
{
    Task<IReadOnlyList<EarnedAchievement>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Só os dublês de teste preenchem; o adapter real sempre devolve null.</summary>
    Task<EarnedAchievement?> GetNewlyUnlockedAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapter fino de <see cref="IStudentAchievementService"/>: <c>ListAsync</c> mapeia o DTO 1:1.
/// O aviso de desbloqueio não passa por este serviço (é o <c>NewlyUnlockedAchievement</c> do pass).
/// </summary>
public sealed class StudentAchievementSource(IStudentAchievementService service) : IStudentAchievementSource
{
    public async Task<IReadOnlyList<EarnedAchievement>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var items = await service.ListAsync(userId, cancellationToken);
        return items.Select(ToAchievement).ToList();
    }

    public Task<EarnedAchievement?> GetNewlyUnlockedAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<EarnedAchievement?>(null);

    public static EarnedAchievement ToAchievement(EarnedAchievementDto dto) =>
        new(dto.Title, dto.Description, dto.EarnedAt);
}

/// <summary>
/// Guarda o selo que ESTE pass acabou de conceder, até o GET /conclusao consumir (uma vez).
/// </summary>
public interface IJustUnlockedAchievementBuffer
{
    void Set(Guid userId, EarnedAchievement achievement);

    EarnedAchievement? Take(Guid userId);
}

public sealed class JustUnlockedAchievementBuffer : IJustUnlockedAchievementBuffer
{
    private readonly ConcurrentDictionary<Guid, EarnedAchievement> _items = new();

    public void Set(Guid userId, EarnedAchievement achievement) => _items[userId] = achievement;

    public EarnedAchievement? Take(Guid userId) => _items.TryRemove(userId, out var item) ? item : null;
}

/// <summary>Dublê vazio para testes de controller que não exercitam selos.</summary>
public sealed class EmptyStudentAchievementSource : IStudentAchievementSource
{
    public static readonly EmptyStudentAchievementSource Instance = new();

    public Task<IReadOnlyList<EarnedAchievement>> ListAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EarnedAchievement>>([]);

    public Task<EarnedAchievement?> GetNewlyUnlockedAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<EarnedAchievement?>(null);
}
