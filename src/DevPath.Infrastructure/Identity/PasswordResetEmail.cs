using System.Net;
using DevPath.Application.Email;

namespace DevPath.Infrastructure.Identity;

/// <summary>Texto do e-mail de recuperação de senha (PT-BR, HTML + texto).</summary>
public static class PasswordResetEmail
{
    public const string Subject = "DevPath: crie sua nova senha";

    public static EmailMessage Build(string to, string displayName, string link, TimeSpan lifespan)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "Olá!" : $"Olá, {displayName.Trim()}!";
        var validity = DescribeLifespan(lifespan);

        var text =
            $"""
            {name}

            Recebemos um pedido para criar uma senha nova na sua conta do DevPath.
            Para escolher a senha nova, abra este link:

            {link}

            O link vale por {validity} e só pode ser usado uma vez.
            Seu progresso e seus pontos continuam guardados.

            Se não foi você, ignore este e-mail: sua senha atual continua valendo.

            Equipe DevPath
            """;

        var htmlName = WebUtility.HtmlEncode(name);
        var htmlLink = WebUtility.HtmlEncode(link);
        var html =
            $"""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head><meta charset="utf-8"><title>{WebUtility.HtmlEncode(Subject)}</title></head>
            <body style="margin:0;padding:24px;background:#f1f5f9;font-family:Arial,Helvetica,sans-serif;color:#0f172a;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px;">
                <tr><td>
                  <p style="font-size:18px;font-weight:bold;margin:0 0 16px;">{htmlName}</p>
                  <p style="font-size:16px;line-height:1.5;margin:0 0 24px;">Recebemos um pedido para criar uma senha nova na sua conta do DevPath.</p>
                  <p style="margin:0 0 24px;">
                    <a href="{htmlLink}" style="display:inline-block;background:#4f46e5;color:#ffffff;text-decoration:none;font-weight:bold;font-size:16px;padding:12px 24px;border-radius:8px;">Criar nova senha</a>
                  </p>
                  <p style="font-size:14px;line-height:1.5;margin:0 0 8px;">O link vale por {validity} e só pode ser usado uma vez. Seu progresso e seus pontos continuam guardados.</p>
                  <p style="font-size:14px;line-height:1.5;margin:0 0 8px;">Se o botão não funcionar, copie e cole este endereço no navegador:</p>
                  <p style="font-size:13px;line-height:1.5;margin:0 0 24px;word-break:break-all;"><a href="{htmlLink}" style="color:#4f46e5;">{htmlLink}</a></p>
                  <p style="font-size:14px;line-height:1.5;margin:0;color:#475569;">Se não foi você, ignore este e-mail: sua senha atual continua valendo.</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        return new EmailMessage(to, Subject, html, text);
    }

    internal static string DescribeLifespan(TimeSpan lifespan)
    {
        if (lifespan.TotalHours >= 1 && lifespan.TotalHours == Math.Floor(lifespan.TotalHours))
        {
            var hours = (int)lifespan.TotalHours;
            return hours == 1 ? "1 hora" : $"{hours} horas";
        }

        var minutes = Math.Max(1, (int)Math.Round(lifespan.TotalMinutes));
        return minutes == 1 ? "1 minuto" : $"{minutes} minutos";
    }
}
