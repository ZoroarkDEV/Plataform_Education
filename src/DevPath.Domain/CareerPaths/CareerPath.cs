using DevPath.Domain.Common;

namespace DevPath.Domain.CareerPaths;

/// <summary>
/// Uma trilha de carreira (ex.: "Backend .NET") que agrupa os módulos que o aluno vai seguir.
/// </summary>
/// <remarks>
/// A entidade protege as próprias regras: não existe <c>CareerPath</c> sem título,
/// e o slug (usado na URL) nasce do título. Depois de criado, o slug é estável:
/// mudar o título não muda a URL, a menos que <see cref="ChangeSlug"/> seja chamado.
/// </remarks>
public sealed class CareerPath
{
    // O EF Core preenche esta lista pelo campo privado; de fora, só leitura.
    private readonly List<Module> _modules = [];

    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? Icon { get; private set; }
    public DifficultyLevel DifficultyLevel { get; private set; }
    public int EstimatedHours { get; private set; }
    public bool IsActive { get; private set; }
    public int Order { get; private set; }

    public IReadOnlyCollection<Module> Modules => _modules;

    // Construtor vazio exigido pelo EF Core para materializar a entidade.
    private CareerPath() { }

    /// <summary>Cria uma nova trilha validando o título.</summary>
    /// <exception cref="ArgumentException">
    /// Quando o título é vazio, só tem espaços ou não gera um slug válido (ex.: "!!!").
    /// </exception>
    public static CareerPath Create(
        string title,
        string description = "",
        DifficultyLevel difficultyLevel = DifficultyLevel.Beginner,
        int estimatedHours = 0,
        int order = 0,
        string? icon = null)
    {
        var careerPath = new CareerPath
        {
            Id = Guid.NewGuid(),
            Slug = SlugGenerator.Generate(title),
            IsActive = true
        };

        // Create e UpdateDetails usam a mesma validação: as regras ficam num lugar só.
        careerPath.UpdateDetails(title, description, difficultyLevel, estimatedHours, order, icon);
        return careerPath;
    }

    /// <summary>Atualiza os dados da trilha (ex.: pelo seed). O slug NÃO muda.</summary>
    public void UpdateDetails(
        string title,
        string description,
        DifficultyLevel difficultyLevel,
        int estimatedHours,
        int order,
        string? icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedHours);
        ArgumentOutOfRangeException.ThrowIfNegative(order);

        Title = title.Trim();
        Description = description.Trim();
        DifficultyLevel = difficultyLevel;
        EstimatedHours = estimatedHours;
        Order = order;
        Icon = icon;
    }

    /// <summary>Troca o slug de propósito (muda a URL da trilha). O valor é normalizado.</summary>
    public void ChangeSlug(string slug) => Slug = SlugGenerator.Generate(slug);

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>Adiciona um módulo no fim da trilha (a ordem é calculada aqui).</summary>
    public Module AddModule(string title, string description = "", int estimatedMinutes = 0)
    {
        var module = Module.Create(Id, title, order: _modules.Count + 1, description, estimatedMinutes);
        _modules.Add(module);
        return module;
    }
}
