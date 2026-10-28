using System.Net;
using DevPath.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace DevPath.IntegrationTests.Web;

/// <summary>GET /health, usado pelo healthcheck do serviço web no docker-compose (issue #21).</summary>
[Collection(WebAppCollection.Name)]
public sealed class HealthEndpointTests(DevPathWebApplicationFactory factory)
{
    [Fact]
    public async Task Health_ComBancoNoAr_Retorna200Healthy()
    {
        var client = factory.CreateBrowserClient();

        var response = await client.GetAsync("/health");

        // Público (sem login) e sem redirecionamento: o healthcheck do Docker só olha o 200.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
