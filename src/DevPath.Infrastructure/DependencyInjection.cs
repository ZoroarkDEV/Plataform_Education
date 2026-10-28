using DevPath.Application.Auth;
using DevPath.Application.CodeExecution;
using DevPath.Application.Journey;
using DevPath.Application.Lessons;
using DevPath.Infrastructure.CodeExecution;
using DevPath.Infrastructure.Identity;
using DevPath.Infrastructure.Journey;
using DevPath.Infrastructure.Lessons;
using DevPath.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace DevPath.Infrastructure;

/// <summary>
/// Registro dos serviços de infraestrutura, separado por PROCESSO (ADR-006):
/// <list type="bullet">
/// <item><see cref="AddInfrastructure"/>: a Web (banco, Identity, aulas, fila de execução). NÃO tem Docker.</item>
/// <item><see cref="AddSandboxExecution"/>: o Worker (banco, sandbox Docker, runner de SQL, laço da fila).</item>
/// </list>
/// A separação é a regra de segurança: só o Worker consegue executar código de aluno.
/// </summary>
public static class DependencyInjection
{
    public const string ConnectionStringName = "DevPath";
    public const string RedisConnectionStringName = "Redis";

    /// <summary>Serviços da Web. Chamado uma vez no Program.cs do DevPath.Web.</summary>
    /// <remarks>
    /// Connection strings: <c>ConnectionStrings:DevPath</c> (PostgreSQL) e <c>ConnectionStrings:Redis</c>.
    /// Em desenvolvimento, use variáveis de ambiente (<c>ConnectionStrings__DevPath</c>,
    /// <c>ConnectionStrings__Redis</c>, veja o .env.example); nunca coloque senha no appsettings.
    /// O cookie (caminhos de login, expiração) é configurado na Web, que é quem conhece as rotas.
    /// </remarks>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddIdentityServices();

        // Leitura das aulas para a página /trilhas/{trilha}/{módulo}/{aula}. Scoped = um por requisição,
        // o mesmo tempo de vida do DbContext que ele usa.
        services.AddScoped<ILessonContentService, LessonContentService>();

        // Onboarding + dashboard (#11). A Web adapta para IStudentJourneyGateway.
        services.AddScoped<IStudentJourneyService, StudentJourneyService>();

        // Execução de código: a Web só ENFILEIRA (Redis) e decide quem pode executar. Quem executa é o Worker.
        services.AddRedis(configuration);
        services.Configure<ExecutionQueueOptions>(configuration.GetSection(ExecutionQueueOptions.SectionName));
        services.AddSingleton<IExecutionQueue, RedisExecutionQueue>();
        services.AddScoped<IExerciseCatalog, ExerciseCatalog>();
        services.AddSingleton<ILessonAccessPolicy, LessonAccessPolicy>();

        return services;
    }

    /// <summary>
    /// Serviços do Worker de execução (ADR-004/ADR-006): o ÚNICO processo que fala com o Docker.
    /// Chamado no Program.cs do DevPath.Worker.
    /// </summary>
    public static IServiceCollection AddSandboxExecution(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddRedis(configuration);
        services.Configure<ExecutionQueueOptions>(configuration.GetSection(ExecutionQueueOptions.SectionName));

        // Singleton: o limite de execuções simultâneas (semáforo) precisa ser o mesmo objeto para o processo todo.
        services.Configure<SandboxOptions>(configuration.GetSection(SandboxOptions.SectionName));
        services.AddSingleton<ICodeSandbox, DockerCliSandbox>();
        services.AddSingleton<ICodeRunner, PostgresSqlCodeRunner>();
        services.AddScoped<IDatasetRepository, DatasetRepository>();
        services.AddScoped<IExerciseSubmissionService, ExerciseSubmissionService>();
        services.AddHostedService<ExecutionWorker>();

        return services;
    }

    /// <summary>Banco de dados (EF Core + PostgreSQL), usado pela Web e pelo Worker.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
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

    /// <summary>
    /// Conexão com o Redis, criada só no primeiro uso. Assim a Web sobe mesmo com o Redis fora do ar
    /// (só a execução de exercícios fica indisponível) e reconecta sozinha quando ele volta.
    /// </summary>
    private static void AddRedis(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            var connectionString = configuration.GetConnectionString(RedisConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{RedisConnectionStringName}' não configurada. " +
                    "Defina a variável de ambiente ConnectionStrings__Redis (ex.: localhost:6379).");
            }

            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false; // não derruba quem chamou; tenta reconectar em segundo plano
            return ConnectionMultiplexer.Connect(options);
        });
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
