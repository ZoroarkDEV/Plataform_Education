namespace DevPath.Domain.CareerPaths;

/// <summary>
/// Uma trilha de carreira (ex.: "Backend .NET") que agrupa os cursos que o aluno vai seguir.
/// </summary>
/// <remarks>
/// A entidade protege as próprias regras: não existe <c>CareerPath</c> sem título,
/// e o slug (usado na URL) é sempre derivado do título, nunca informado à mão.
/// </remarks>
public sealed class CareerPath
{
    public Guid Id { get; }
    public string Title { get; }
    public string Slug { get; }

    private CareerPath(Guid id, string title, string slug)
    {
        Id = id;
        Title = title;
        Slug = slug;
    }

    /// <summary>Cria uma nova trilha validando o título.</summary>
    /// <exception cref="ArgumentException">Quando o título é vazio ou só tem espaços.</exception>
    public static CareerPath Create(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("O título da trilha é obrigatório.", nameof(title));
        }

        var normalizedTitle = title.Trim();
        return new CareerPath(Guid.NewGuid(), normalizedTitle, ToSlug(normalizedTitle));
    }

    // "Backend .NET" -> "backend-net"
    private static string ToSlug(string value)
    {
        var words = value
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0);

        return string.Join('-', words);
    }
}
