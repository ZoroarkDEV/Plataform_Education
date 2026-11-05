using System.Globalization;
using System.Text;

namespace DevPath.Application.Lessons;

/// <summary>
/// Nome legível de um dataset para a tela (#27 / #30 I6 follow-up, ADR-007).
/// Preferência: <c>Datasets.Name</c> (preenchido pelo seed no #24). Fallback: slug humanizado.
/// </summary>
public static class DatasetDisplayNames
{
    /// <summary>
    /// <paramref name="name"/> se não for vazio; senão humaniza <paramref name="slug"/>
    /// (<c>livraria</c> → "Livraria", <c>loja_online</c> / <c>loja-online</c> → "Loja online").
    /// Null quando não há dataset.
    /// </summary>
    public static string? Resolve(string? slug, string? name = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return HumanizeSlug(slug.Trim());
    }

    /// <summary>Troca <c>_</c> e <c>-</c> por espaço e capitaliza só a primeira letra (pt-BR).</summary>
    public static string HumanizeSlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        var words = slug.Trim().Replace('-', '_').Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return slug.Trim();
        }

        var sb = new StringBuilder();
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            var word = words[i].ToLowerInvariant();
            if (i == 0 && word.Length > 0)
            {
                sb.Append(char.ToUpper(word[0], CultureInfo.InvariantCulture));
                if (word.Length > 1)
                {
                    sb.Append(word.AsSpan(1));
                }
            }
            else
            {
                sb.Append(word);
            }
        }

        return sb.ToString();
    }
}
