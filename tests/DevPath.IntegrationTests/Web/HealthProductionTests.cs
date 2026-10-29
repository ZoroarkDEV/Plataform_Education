using System.Net;
using DevPath.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DevPath.IntegrationTests.Web;

/// <summary>
/// Em Production o healthcheck do container chama http://127.0.0.1:8080/health por dentro, sem proxy nem HTTPS.
/// O /health não pode cair no redirecionamento para HTTPS (307), senão o container nunca fica healthy (#21).
/// </summary>
[Collection(ProductionAppCollection.Name)]
public sealed class HealthProductionTests(ProductionBehindProxyFactory factory)
{
    [Fact]
    public async Task Health_EmHttpNaProducao_Retorna200SemRedirecionar()
    {
        var client = factory.CreateProxyClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "o banco do teste está no ar e o /health não redireciona");
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/healthz")] // só o segmento /health é isento, não qualquer caminho que comece com "health"
    public async Task OutrasRotas_EmHttpNaProducao_ContinuamRedirecionandoParaHttps(string path)
    {
        var client = factory.CreateProxyClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location!.Scheme.Should().Be("https");
    }
}
