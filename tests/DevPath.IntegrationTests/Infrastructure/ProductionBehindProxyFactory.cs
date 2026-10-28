using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.IntegrationTests.Infrastructure;

/// <summary>
/// A app em "Production" atrás de um proxy reverso: o HTTPS termina no proxy, a app recebe HTTP
/// com o cabeçalho X-Forwarded-Proto: https.
/// </summary>
/// <remarks>
/// O TestServer não tem IP de origem. Um IStartupFilter roda ANTES do pipeline da app e define o IP
/// da conexão, simulando de onde a requisição veio. O padrão é 127.0.0.1, um proxy confiável
/// (o ASP.NET Core confia em loopback por padrão).
/// </remarks>
public sealed class ProductionBehindProxyFactory : DevPathWebApplicationFactory
{
    /// <summary>Cabeçalho que os testes usam para escolher o IP de origem simulado.</summary>
    public const string SimulatedRemoteIpHeader = "X-Test-Remote-Ip";

    protected override string EnvironmentName => "Production";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, SimulatedRemoteIpStartupFilter>());
    }

    /// <summary>Cliente que fala HTTP com um host "de verdade" (o HSTS ignora localhost).</summary>
    public HttpClient CreateProxyClient() =>
        CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("http://devpath.example")
        });

    private sealed class SimulatedRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, Func<Task> nextMiddleware) =>
            {
                var ip = context.Request.Headers[SimulatedRemoteIpHeader].ToString();
                context.Connection.RemoteIpAddress = string.IsNullOrEmpty(ip) ? IPAddress.Loopback : IPAddress.Parse(ip);
                return nextMiddleware();
            });
            next(app);
        };
    }
}

[CollectionDefinition(Name)]
public sealed class ProductionAppCollection : ICollectionFixture<ProductionBehindProxyFactory>
{
    public const string Name = "ProductionApp";
}
