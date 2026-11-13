using System.ComponentModel.DataAnnotations;
using System.Globalization;
using DevPath.Application.Email;
using DevPath.Web.Models.Lessons;

namespace DevPath.Web.ViewModels.Admin;

/// <summary>
/// Campos do formulário de /admin/email (#47). Os nomes são os mesmos do <see cref="UpdateEmailSettingsCommand"/>,
/// então os <c>Errors</c> do serviço caem direto no ModelState. A validação é do serviço (Application).
/// </summary>
public class EmailSettingsForm
{
    /// <summary>Texto do select (None, Smtp, Resend). String de propósito: valor estranho vira o erro PT-BR do serviço.</summary>
    [Display(Name = "Provedor")]
    public string? Provider { get; set; }

    [Display(Name = "E-mail do remetente")]
    public string? FromAddress { get; set; }

    [Display(Name = "Nome do remetente")]
    public string? FromName { get; set; }

    /// <summary>Só entra no POST. A tela NUNCA devolve este valor (nem no HTML, nem no ModelState, nem no TempData).</summary>
    [Display(Name = "Nova chave da API do Resend")]
    [DataType(DataType.Password)]
    public string? NewResendApiKey { get; set; }

    [Display(Name = "Remover a chave salva")]
    public bool ClearResendApiKey { get; set; }

    /// <summary>Valor que vai para o serviço. Desconhecido = fora do enum (o serviço responde "Escolha None, Smtp ou Resend.").</summary>
    public EmailProviderKind ParseProvider() =>
        Enum.TryParse<EmailProviderKind>(Provider, ignoreCase: true, out var kind) && Enum.IsDefined(kind)
            && !int.TryParse(Provider, out _)
            ? kind
            : (EmailProviderKind)(-1);

    public UpdateEmailSettingsCommand ToCommand() =>
        new(ParseProvider(), FromAddress, FromName, NewResendApiKey, ClearResendApiKey);

    /// <summary>Campos que têm &lt;span asp-validation-for&gt; na tela; outra chave do serviço vira erro geral.</summary>
    public static readonly IReadOnlySet<string> FieldNames = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(Provider), nameof(FromAddress), nameof(FromName), nameof(NewResendApiKey), nameof(ClearResendApiKey)
    };
}

/// <summary>Tela inteira: o formulário + o que o serviço diz que está salvo e em uso.</summary>
public sealed class EmailSettingsPageViewModel : EmailSettingsForm
{
    public required EmailSettingsDto Settings { get; init; }

    /// <summary>Mensagem do PRG depois de salvar (uma vez).</summary>
    public string? SavedMessage { get; init; }

    /// <summary>Resultado do e-mail de teste (uma vez, depois do PRG).</summary>
    public EmailTestNotice? TestNotice { get; init; }

    public static EmailSettingsPageViewModel From(EmailSettingsDto settings, EmailSettingsForm? posted = null,
        string? savedMessage = null, EmailTestNotice? testNotice = null) => new()
    {
        Settings = settings,
        Provider = posted?.Provider ?? settings.Provider.ToString(),
        FromAddress = posted is null ? settings.FromAddress : posted.FromAddress,
        FromName = posted is null ? settings.FromName : posted.FromName,
        NewResendApiKey = null, // sempre vazio
        ClearResendApiKey = posted?.ClearResendApiKey ?? false,
        SavedMessage = savedMessage,
        TestNotice = testNotice
    };
}

/// <summary>Resultado do e-mail de teste como a tela mostra. <see cref="IsError"/> = role="alert".</summary>
public sealed record EmailTestNotice(EmailTestStatus Status, string Title, string? Detail)
{
    public bool IsError => Status is not EmailTestStatus.Sent;
}

/// <summary>Textos PT-BR da tela /admin/email (#47), num lugar só para UX revisar e os testes afirmarem.</summary>
public static class EmailSettingsTexts
{
    public const string PageTitle = "Configuração de e-mail";

    public const string Intro =
        "Escolha quem envia os e-mails do DevPath (como o de recuperação de senha) e com qual remetente. " +
        "O que você salvar vale a partir do próximo envio, sem reiniciar.";

    public const string Saved = "Configuração de e-mail salva.";

    public const string CurrentHeading = "Em uso agora";
    public const string FormHeading = "Configuração salva";
    public const string TestHeading = "E-mail de teste";

    public const string Locked =
        "Este ambiente usa só a configuração do servidor (Email:LockedToConfiguration). Em desenvolvimento e " +
        "staging os e-mails continuam indo para o Mailpit: o que você salvar aqui fica guardado, mas só passa a " +
        "valer num ambiente sem essa trava (produção).";

    public const string MissingLead = "O envio de e-mails não está pronto. Falta:";

    public const string RestartRequired =
        "Parte do que falta só se resolve no servidor: ajuste as variáveis de ambiente (Email__*) e reinicie a " +
        "aplicação. O que você salva nesta tela vale sem reiniciar.";

    public const string ReadyToSend = "Pronto para enviar";
    public const string NotReadyToSend = "Envio indisponível";

    public const string FromAddressHint =
        "No Resend, o endereço precisa ser de um domínio já verificado no painel do Resend.";

    public const string FromNameHint = "Opcional. Em branco, usamos \"DevPath\".";

    public const string KeyHint =
        "Deixe em branco para manter a chave salva. Por segurança, a chave nunca aparece de novo: para trocar, cole uma nova.";

    public const string KeyNotConfigured = "Nenhuma chave configurada.";

    public const string KeyFromEnvironment =
        "Essa chave vem da variável de ambiente do servidor. Uma chave salva aqui passa a valer no lugar dela.";

    public const string ClearKeyHint = "Apaga a chave salva por esta tela. Não dá para remover e colar uma nova ao mesmo tempo.";

    public const string SaveButton = "Salvar configuração";

    public const string TestIntro =
        "Envia uma mensagem para o seu e-mail com a configuração em uso agora. Dá para enviar um teste por minuto.";

    public const string TestButton = "Enviar e-mail de teste";

    public const string TestNotSentTitle = "O e-mail de teste não foi enviado.";
    public const string TestFailedTitle = "O envio do e-mail de teste falhou.";
    public const string TestForbidden = "Só administradores podem enviar o e-mail de teste.";

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public static string ProviderLabel(EmailProviderKind provider) => provider switch
    {
        EmailProviderKind.None => "Nenhum (não envia e-mails)",
        EmailProviderKind.Smtp => "SMTP (servidor configurado no ambiente)",
        EmailProviderKind.Resend => "Resend",
        _ => provider.ToString()
    };

    public static string ProviderName(EmailProviderKind provider) => provider switch
    {
        EmailProviderKind.None => "Nenhum",
        EmailProviderKind.Smtp => "SMTP",
        _ => provider.ToString()
    };

    public static string SourceLabel(EmailSettingsSource source) => source switch
    {
        EmailSettingsSource.Database => "esta tela",
        _ => "configuração do servidor (variáveis de ambiente)"
    };

    /// <summary>"Chave configurada (…abcd)"; sem os 4 últimos (chave curta), só "Chave configurada".</summary>
    public static string KeyConfigured(string? hint) =>
        string.IsNullOrEmpty(hint) ? "Chave configurada" : $"Chave configurada (…{hint})";

    /// <summary>"Alterado por Ana em 12 de novembro de 2026 às 11:54" (America/Sao_Paulo, como o "Ganho em" do dashboard).</summary>
    public static string ChangedBy(string? displayName, DateTimeOffset at)
    {
        var when = $"{AchievementTexts.FormatDate(at)} às {AchievementTexts.ToSaoPaulo(at).ToString("HH:mm", PortugueseBrazil)}";
        return string.IsNullOrWhiteSpace(displayName)
            ? $"Alterado em {when} por uma conta que não existe mais"
            : $"Alterado por {displayName} em {when}";
    }

    /// <summary>Data/hora local (America/Sao_Paulo) em ISO para o atributo datetime.</summary>
    public static string ChangedAtIso(DateTimeOffset at) =>
        AchievementTexts.ToSaoPaulo(at).ToString("yyyy-MM-dd'T'HH:mmzzz", CultureInfo.InvariantCulture);

    /// <summary>"Espere 45 segundos"; arredonda para cima e nunca menos de 1.</summary>
    public static string RateLimited(TimeSpan? retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling((retryAfter ?? TimeSpan.FromMinutes(1)).TotalSeconds));
        var unit = seconds == 1 ? "segundo" : "segundos";
        return $"Você enviou um teste há pouco. Espere {seconds} {unit} para enviar outro.";
    }

    public static EmailTestNotice ToNotice(EmailTestResult result) => result.Status switch
    {
        EmailTestStatus.Sent => new(result.Status,
            $"E-mail de teste enviado para {result.SentTo} pelo {ProviderName(result.Provider)}.",
            "Confira a caixa de entrada e o spam."),
        EmailTestStatus.NotConfigured => new(result.Status, TestNotSentTitle, result.Message),
        EmailTestStatus.Failed => new(result.Status, TestFailedTitle, result.Message),
        EmailTestStatus.RateLimited => new(result.Status, RateLimited(result.RetryAfter), null),
        _ => new(EmailTestStatus.Forbidden, TestForbidden, null)
    };
}
