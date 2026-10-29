using DevPath.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Infrastructure;

/// <summary>
/// Sobe a aplicação Web inteira em memória (WebApplicationFactory), apontando para um
/// PostgreSQL descartável do Testcontainers, com as migrations aplicadas.
/// Compartilhado entre os testes da coleção <see cref="WebAppCollection"/> (um container só).
/// </summary>
public class DevPathWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Mesma chave que a aplicação lê: ConnectionStrings:DevPath.
        builder.UseSetting("ConnectionStrings:DevPath", _postgres.GetConnectionString());
        builder.UseEnvironment(EnvironmentName);
    }

    /// <summary>Ambiente da aplicação nos testes. "Testing" se comporta como produção (não é Development).</summary>
    protected virtual string EnvironmentName => "Testing";

    /// <summary>Cliente que guarda cookies e NÃO segue redirects, para podermos verificar o 302.</summary>
    /// <remarks>
    /// Usa https:// porque o ambiente "Testing" se comporta como produção: cookie Secure e
    /// redirecionamento para HTTPS. Em http:// o cookie de login nem seria reenviado.
    /// </remarks>
    public HttpClient CreateBrowserClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

    public virtual async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class WebAppCollection : ICollectionFixture<DevPathWebApplicationFactory>
{
    public const string Name = "WebApp";
}
