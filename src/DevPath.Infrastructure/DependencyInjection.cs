using DevPath.Application.Auth;
using DevPath.Application.Journey;
using DevPath.Application.Lessons;
using DevPath.Infrastructure.Identity;
using DevPath.Infrastructure.Journey;
using DevPath.Infrastructure.Lessons;
using DevPath.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevPath.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DevPath";

    /// <summary>
    /// Registra os serviços de infraestrutura: banco (EF Core) e ASP.NET Identity.
    /// Chamado uma vez no Program.cs do DevPath.Web.
    /// </summary>
    /// <remarks>
    /// A connection string vem de <c>ConnectionStrings:DevPath</c>. Em desenvolvimento,
    /// defina a variável de ambiente <c>ConnectionStrings__DevPath</c> (veja o .env.example);
    /// nunca coloque a senha no appsettings.
    /// O cookie (caminhos de login, expiração) é configurado na Web, que é quem conhece as rotas.
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

        services.AddIdentityServices();

        // Leitura das aulas para a página /trilhas/{trilha}/{módulo}/{aula}. Scoped = um por requisição,
        // o mesmo tempo de vida do DbContext que ele usa.
        services.AddScoped<ILessonContentService, LessonContentService>();

        // Onboarding + dashboard (#11). A Web adapta para IStudentJourneyGateway.
        services.AddScoped<IStudentJourneyService, StudentJourneyService>();

        return services;
    }

    private static void AddIdentityServices(this IServiceCollection services)
    {
        // AddIdentity registra UserManager, SignInManager e o esquema de cookie "Identity.Application".
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                // Senha: no mínimo 8 caracteres com número, minúscula e maiúscula. Símbolo é opcional.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                // Lockout: 5 senhas erradas bloqueiam a conta por 15 minutos (freia força bruta).
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;

                // Confirmação de e-mail fica para quando houver envio de e-mail.
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddEntityFrameworkStores<DevPathDbContext>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<PortugueseIdentityErrorDescriber>()
            .AddClaimsPrincipalFactory<DevPathUserClaimsPrincipalFactory>();

        services.AddScoped<IAuthService, AuthService>();
    }
}
