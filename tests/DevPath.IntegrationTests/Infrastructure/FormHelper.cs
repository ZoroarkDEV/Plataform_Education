using System.Net;
using System.Text.RegularExpressions;

namespace DevPath.IntegrationTests.Infrastructure;

/// <summary>
/// Simula um navegador enviando um formulário com antiforgery:
/// faz GET na página, lê o campo oculto __RequestVerificationToken e envia junto no POST.
/// (O cookie de antiforgery volta sozinho porque o HttpClient guarda cookies.)
/// </summary>
public static partial class FormHelper
{
    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryTokenRegex();

    public static async Task<HttpResponseMessage> SubmitFormAsync(
        this HttpClient client, string formPageUrl, string postUrl, IDictionary<string, string> fields)
    {
        var page = await client.GetAsync(formPageUrl);
        page.StatusCode.Should().Be(HttpStatusCode.OK, $"a página {formPageUrl} deveria abrir");

        var html = await page.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex().Match(html);
        match.Success.Should().BeTrue($"a página {formPageUrl} deveria ter o token antiforgery");

        var form = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value)
        };

        return await client.PostAsync(postUrl, new FormUrlEncodedContent(form));
    }

    /// <summary>
    /// Lê o HTML já decodificado: o Razor escapa acentos ("não" vira "n&amp;#xE3;o"),
    /// então decodificamos antes de procurar textos em português.
    /// </summary>
    public static async Task<string> ReadHtmlAsync(this HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
}
