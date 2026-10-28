using System.Globalization;
using System.Text;
using DevPath.Domain.Common;

namespace DevPath.Domain.CareerPaths;

/// <summary>
/// Uma trilha de carreira (ex.: "Backend .NET") que agrupa os módulos que o aluno vai seguir.
/// </summary>
/// <remarks>
/// A entidade protege as próprias regras: não existe <c>CareerPath</c> sem título,
/// e o slug (usado na URL) é sempre derivado do título, nunca informado à mão.
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
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedHours);
        ArgumentOutOfRangeException.ThrowIfNegative(order);

        var normalizedTitle = title.Trim();
        var slug = ToSlug(normalizedTitle);
        if (slug.Length == 0)
        {
            throw new ArgumentException("O título precisa ter ao menos uma letra ou número.", nameof(title));
        }

        return new CareerPath
        {
            Id = Guid.NewGuid(),
            Title = normalizedTitle,
            Slug = slug,
            Description = description.Trim(),
            Icon = icon,
            DifficultyLevel = difficultyLevel,
            EstimatedHours = estimatedHours,
            IsActive = true,
            Order = order
        };
    }

    /// <summary>Adiciona um módulo no fim da trilha (a ordem é calculada aqui).</summary>
    public Module AddModule(string title, string description = "", int estimatedMinutes = 0)
    {
        var module = Module.Create(Id, title, order: _modules.Count + 1, description, estimatedMinutes);
        _modules.Add(module);
        return module;
    }

    // "Programação  Backend .NET" -> "programacao-backend-net"
    private static string ToSlug(string value)
    {
        var words = RemoveAccents(value)
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsAsciiLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0);

        return string.Join('-', words);
    }

    // FormD separa "ç" em "c" + cedilha; depois descartamos as marcas (NonSpacingMark).
    private static string RemoveAccents(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var withoutMarks = decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(withoutMarks).Normalize(NormalizationForm.FormC);
    }
}
