using System.Globalization;

namespace DevPath.Web.Models.Lessons;

/// <summary>
/// Textos do selo "Primeiro SELECT" (#45; PO: roteiro-modulo2-sql.md, US-BADGE.1).
/// O seed do Backend já grava o mesmo título e a mesma descrição; estas constantes
/// existem para a UI e para os testes afirmarem o texto do PO.
/// </summary>
public static class AchievementTexts
{
    public const string Medal = "🏅";

    public const string UnlockLead = "Selo desbloqueado: ";

    public const string UnlockAfterTitle = ". ";

    public const string FirstSelectTitle = "Primeiro SELECT";

    public const string FirstSelectDescription =
        "Você fez um banco de dados responder à sua primeira pergunta.";

    public const string SectionHeading = "Seus selos";

    public static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Texto completo do aviso, sem o emoji (ele é visual e aria-hidden).</summary>
    public static string UnlockNotice(string title, string description) =>
        $"{UnlockLead}{title}{UnlockAfterTitle}{description}";

    public static string FirstSelectUnlockNotice =>
        UnlockNotice(FirstSelectTitle, FirstSelectDescription);

    /// <summary>Data no fuso America/Sao_Paulo, em pt-BR (ex.: "11 de novembro de 2026").</summary>
    public static string FormatDate(DateTimeOffset earnedAt) =>
        ToSaoPaulo(earnedAt).ToString("d 'de' MMMM 'de' yyyy", PortugueseBrazil);

    /// <summary>Data local (America/Sao_Paulo) em ISO, para o atributo <c>datetime</c> do <c>time</c>.</summary>
    public static string FormatDateIso(DateTimeOffset earnedAt) =>
        ToSaoPaulo(earnedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string EarnedOn(DateTimeOffset earnedAt) => $"Ganho em {FormatDate(earnedAt)}";

    public static DateTimeOffset ToSaoPaulo(DateTimeOffset earnedAt) =>
        TimeZoneInfo.ConvertTime(earnedAt, SaoPaulo);
}
