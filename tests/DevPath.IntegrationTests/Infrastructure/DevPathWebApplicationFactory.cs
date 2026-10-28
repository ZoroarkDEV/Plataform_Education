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
public sealed class DevPathWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Mesma chave que a aplicação lê: ConnectionStrings:DevPath.
        builder.UseSetting("ConnectionStrings:DevPath", _postgres.GetConnectionString());
        builder.UseEnvironment("Testing");
    }

    /// <summary>Cliente que guarda cookies e NÃO segue redirects, para podermos verificar o 302.</summary>
    public HttpClient CreateBrowserClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task InitializeAsync()
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
