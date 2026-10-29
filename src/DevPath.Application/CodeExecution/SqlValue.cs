using System.Globalization;
using System.Text.Json;

namespace DevPath.Application.CodeExecution;

/// <summary>Tipo de um valor de resultado, do jeito que o JSON o representa.</summary>
public enum SqlValueKind
{
    Null,
    Text,
    Number,
    Bool,

    /// <summary>Array ou objeto (ex.: uma coluna <c>int[]</c> ou <c>json</c>). Caso de teste nunca espera isso.</summary>
    Other
}

/// <summary>
/// Um valor de célula: do resultado do aluno ou do caso de teste. As regras de igualdade do PO ficam aqui:
/// número compara pelo VALOR (29.9 == 29.90, 10 == 10.0), null só bate com null, texto e bool são exatos.
/// </summary>
/// <param name="Kind">Tipo do valor.</param>
/// <param name="Raw">Texto do valor: o texto em si, o número como veio no JSON, "true"/"false", ou o JSON bruto.</param>
public sealed record SqlValue(SqlValueKind Kind, string? Raw)
{
    public static readonly SqlValue Null = new(SqlValueKind.Null, null);

    public static SqlValue Text(string value) => new(SqlValueKind.Text, value);
    public static SqlValue Bool(bool value) => new(SqlValueKind.Bool, value ? "true" : "false");
    public static SqlValue Number(decimal value) => new(SqlValueKind.Number, value.ToString(CultureInfo.InvariantCulture));

    public static SqlValue FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => Null,
        JsonValueKind.String => Text(element.GetString()!),
        JsonValueKind.Number => new SqlValue(SqlValueKind.Number, element.GetRawText()),
        JsonValueKind.True => Bool(true),
        JsonValueKind.False => Bool(false),
        _ => new SqlValue(SqlValueKind.Other, element.GetRawText())
    };

    /// <summary>
    /// Chave canônica: dois valores "iguais" pelas regras do PO têm a MESMA chave. Usada para comparar
    /// linhas como multiconjunto (contar quantas vezes cada linha aparece) sem depender da ordem.
    /// </summary>
    public string CanonicalKey => Kind switch
    {
        SqlValueKind.Null => "n",
        SqlValueKind.Number => "#" + CanonicalNumber(Raw!),
        SqlValueKind.Bool => "b" + Raw,
        SqlValueKind.Text => "t" + Raw,
        _ => "o" + Raw
    };

    public bool Matches(SqlValue other) => CanonicalKey == other.CanonicalKey;

    /// <summary>Como o aluno vê o valor no feedback: texto entre aspas, NULL em maiúsculas.</summary>
    public string Display => Kind switch
    {
        SqlValueKind.Null => "NULL",
        SqlValueKind.Text => $"\"{Raw}\"",
        _ => Raw!
    };

    // "29.90" e "29.9" viram "29.9"; "10" e "10.0" viram "10". O decimal guarda a escala (29.90 tem escala 2),
    // então o ToString("G29") é quem descarta os zeros à direita. Fora da faixa do decimal (ex.: 1e400 de um
    // float), cai para double; se nem isso, compara o texto como veio.
    private static string CanonicalNumber(string raw)
    {
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return d == 0 ? "0" : d.ToString("G29", CultureInfo.InvariantCulture);
        }
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && double.IsFinite(f))
        {
            return f.ToString("R", CultureInfo.InvariantCulture);
        }
        return raw;
    }
}
