using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DevPath";

    /// <summary>
    /// Registra os serviços de infraestrutura (banco, e no futuro Redis, e-mail...).
    /// Chamado uma vez no Program.cs do DevPath.Web.
    /// </summary>
    /// <remarks>
    /// A connection string vem de <c>ConnectionStrings:DevPath</c>. Em desenvolvimento,
    /// defina a variável de ambiente <c>ConnectionStrings__DevPath</c> (veja o .env.example);
    /// nunca coloque a senha no appsettings.
    /// </remarks>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' não configurada. " +
                "Defina a variável de ambiente ConnectionStrings__DevPath (veja o .env.example).");
        }

        services.AddDbContext<DevPathDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }
}
