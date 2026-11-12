using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DevPath.Application.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPath.Infrastructure.Email;

/// <summary>
/// Envio pela API do Resend (<c>POST https://api.resend.com/emails</c>), produção (#44).
/// HttpClient tipado puro em vez do SDK: é um endpoint só, e assim não entra dependência nova.
/// </summary>
/// <remarks>
/// Quem escolhe o provedor e passa remetente/chave a cada envio é o <see cref="ConfiguredEmailSender"/> (#47).
/// A chave vai só no header <c>Authorization</c> desta requisição: nunca em log, exceção ou
/// DefaultRequestHeaders. Resposta 4xx/5xx, timeout ou falha de rede viram
/// <see cref="EmailDeliveryException"/> (o <c>PasswordResetEmailWorker</c> registra e continua).
/// </remarks>
public sealed class ResendEmailSender(
    HttpClient httpClient,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    public const string EmailsPath = "emails";
    private const int MaxLoggedBodyLength = 500;

    /// <summary>Envia com o remetente e a chave de <see cref="EmailOptions"/> (uso direto/testes do #44).</summary>
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        return SendAsync(message, new EmailTransport(settings.FromAddress, settings.FromName, settings.Resend.ApiKey), cancellationToken);
    }

    /// <summary>Envia com remetente e chave resolvidos por envio (tela de admin, #47).</summary>
    internal async Task SendAsync(EmailMessage message, EmailTransport transport, CancellationToken cancellationToken)
    {
        var apiKey = transport.ResendApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // O ConfiguredEmailSender já confere antes; isto é só uma rede de segurança.
            throw new EmailDeliveryException("Chave do Resend ausente: cadastre na tela de configurações de e-mail (admin).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, EmailsPath)
        {
            Content = JsonContent.Create(new ResendEmailRequest(
                From: $"{transport.FromName} <{transport.FromAddress}>",
                To: [message.To],
                Subject: message.Subject,
                Html: message.HtmlBody,
                Text: message.TextBody))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Resend não respondeu a tempo ({Timeout} s): e-mail \"{Subject}\" não enviado.",
                httpClient.Timeout.TotalSeconds, message.Subject);
            throw new EmailDeliveryException("Tempo esgotado ao chamar o Resend.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("Falha de rede ao chamar o Resend ({Error}): e-mail \"{Subject}\" não enviado.",
                ex.Message, message.Subject);
            throw new EmailDeliveryException("Falha de rede ao chamar o Resend.", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            // O corpo de erro do Resend é { statusCode, name, message }: não contém a chave.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > MaxLoggedBodyLength)
            {
                body = body[..MaxLoggedBodyLength];
            }

            var status = (int)response.StatusCode;
            logger.LogError(
                "Resend recusou o e-mail \"{Subject}\" com HTTP {Status} ({Kind}): {Body}",
                message.Subject, status, status >= 500 ? "erro do provedor" : "erro da requisição/configuração", body);
            throw new EmailDeliveryException($"Resend respondeu HTTP {status}.", statusCode: status);
        }
    }

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text);
}
