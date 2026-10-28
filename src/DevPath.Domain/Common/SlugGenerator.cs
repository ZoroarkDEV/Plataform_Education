using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace DevPath.Domain.Common;

/// <summary>
/// Gera slugs para URLs a partir de um texto: "Programação  Backend .NET" vira "programacao-backend-net".
/// Usado por CareerPath, Module e Lesson. O seed também usa o slug para encontrar conteúdo existente.
/// </summary>
public static class SlugGenerator
{
    /// <summary>Gera o slug ou lança <see cref="ArgumentException"/> se o resultado ficar vazio (ex.: "!!!").</summary>
    /// <param name="value">Texto de origem (normalmente o título).</param>
    /// <param name="paramName">Preenchido automaticamente com o nome do argumento de quem chamou.</param>
    public static string Generate(string value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        var words = RemoveAccents(value)
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsAsciiLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0);

        var slug = string.Join('-', words);
        if (slug.Length == 0)
        {
            throw new ArgumentException("O texto precisa ter ao menos uma letra ou número.", paramName);
        }

        return slug;
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
