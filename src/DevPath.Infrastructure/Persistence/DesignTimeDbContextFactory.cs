using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DevPath.Infrastructure.Persistence;

/// <summary>
/// Usado só pelas ferramentas do EF (<c>dotnet ef migrations add</c> / <c>database update</c>).
/// </summary>
/// <remarks>
/// Lê a variável de ambiente <c>ConnectionStrings__DevPath</c>. Sem ela, cai no banco local
/// do docker-compose com os valores de exemplo do <c>.env.example</c> (não é um segredo real).
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DevPathDbContext>
{
    private const string LocalDevelopmentConnectionString =
        "Host=localhost;Port=5432;Database=devpath;Username=devpath;Password=change_me";

    public DevPathDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DevPath");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = LocalDevelopmentConnectionString;
        }

        var options = new DbContextOptionsBuilder<DevPathDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new DevPathDbContext(options);
    }
}
