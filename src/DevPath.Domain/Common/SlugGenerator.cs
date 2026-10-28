using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DevPath.Domain.Common;

/// <summary>
/// Gera slugs para URLs a partir de um texto: "Programação  Backend .NET" vira "programacao-backend-net" e "Front-end" vira "front-end".
/// Usado por CareerPath, Module e Lesson. O seed também usa o slug para encontrar conteúdo existente.
/// </summary>
public static partial class SlugGenerator
{
    /// <summary>Gera o slug ou lança <see cref="ArgumentException"/> se o resultado ficar vazio (ex.: "!!!").</summary>
    /// <param name="value">Texto de origem (normalmente o título).</param>
    /// <param name="paramName">Preenchido automaticamente com o nome do argumento de quem chamou.</param>
    public static string Generate(string value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        // Todo caractere que não é letra/número ASCII vira separador: espaço, hífen, ponto, barra, "#"...
        // Sequências de separadores viram UM hífen, e hífens nas pontas são removidos.
        // Assim o algoritmo é idempotente: Generate(Generate(x)) == Generate(x),
        // e ChangeSlug("pgadmin-e-dbeaver") mantém os hífens.
        var slug = NonAlphanumeric().Replace(RemoveAccents(value).ToLowerInvariant(), "-").Trim('-');
        if (slug.Length == 0)
        {
            throw new ArgumentException("O texto precisa ter ao menos uma letra ou número.", paramName);
        }

        return slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

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
