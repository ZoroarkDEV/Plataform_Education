using DevPath.Application.Achievements;
using DevPath.Application.Analytics;
using DevPath.Application.Auth;
using DevPath.Application.CodeExecution;
using DevPath.Application.Email;
using DevPath.Application.Journey;
using DevPath.Application.Lessons;
using DevPath.Application.Progress;
using DevPath.Infrastructure.Achievements;
using DevPath.Infrastructure.Analytics;
using DevPath.Infrastructure.CodeExecution;
using DevPath.Infrastructure.Email;
using DevPath.Infrastructure.Identity;
using DevPath.Infrastructure.Journey;
using DevPath.Infrastructure.Lessons;
using DevPath.Infrastructure.Persistence;
using DevPath.Infrastructure.Progress;
using Microsoft.AspNetCore.DataProtection;
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
        services.AddIdentityServices(configuration);
        services.AddEmailSender(configuration);

        // Leitura das aulas para a página /trilhas/{trilha}/{módulo}/{aula}. Scoped = um por requisição,
        // o mesmo tempo de vida do DbContext que ele usa.
        services.AddScoped<ILessonContentService, LessonContentService>();

        // Onboarding + dashboard (#11). A Web adapta para IStudentJourneyGateway.
        services.AddScoped<IStudentJourneyService, StudentJourneyService>();

        // Progresso de aula (#27, ADR-007): mesma foto da trilha para dashboard, aula e execução.
        services.AddScoped<JourneySnapshotLoader>();
        services.AddScoped<LessonProgressWriter>();
        services.AddScoped<ILessonProgressService, LessonProgressService>();

        // Textos da tela "Módulo concluído" (#42). A Web adapta para IModuleCompletionContentSource.
        services.AddScoped<IModuleCompletionContentService, ModuleCompletionContentService>();

        // Execução de código: a Web só ENFILEIRA (Redis) e decide quem pode executar. Quem executa é o Worker.
        services.AddRedis(configuration);
        services.Configure<ExecutionQueueOptions>(configuration.GetSection(ExecutionQueueOptions.SectionName));
        services.AddSingleton<IExecutionQueue, RedisExecutionQueue>();
        services.AddScoped<IExerciseCatalog, ExerciseCatalog>();
        // Scoped: lê o progresso do aluno no banco (trilha atual + desbloqueio), #27.
        services.AddScoped<ILessonAccessPolicy, LessonAccessPolicy>();

        // Selos do aluno para o dashboard (#45), com o backfill silencioso. A concessão no acerto é do Worker.
        services.AddScoped<IStudentAchievementService, StudentAchievementService>();

        // Relatório de tentativas para o dono (#43, /admin/tentativas).
        services.AddScoped<IAttemptAnalyticsService, AttemptAnalyticsService>();

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

    /// <summary>
    /// E-mail (#44, #47). <see cref="IEmailSender"/> é o <see cref="ConfiguredEmailSender"/>: a cada envio ele resolve
    /// provedor, remetente e chave (configuração <c>Email:*</c> + o que o admin salvou na tela, protegido no banco)
    /// e repassa ao SMTP ou ao Resend. Veja docs/configuracoes-de-email.md.
    /// </summary>
    /// <remarks>
    /// Falha no startup só o que é erro de configuração do SERVIDOR: <c>Email:Provider</c> desconhecido ou
    /// <c>Smtp</c> sem <c>Email:Smtp:Host</c>. <c>Resend</c> sem chave SOBE (#47): a tela mostra o que falta e o
    /// envio diz o motivo (<see cref="EmailDeliveryException"/>), sem derrubar a Web.
    /// </remarks>
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EmailOptions.SectionName);
        // A chave do ambiente: Email__Resend__ApiKey ou RESEND_API_KEY (nome do Resend). A da tela fica no banco.
        var resendApiKey = section["Resend:ApiKey"];
        if (string.IsNullOrWhiteSpace(resendApiKey))
        {
            resendApiKey = configuration[ResendOptions.ApiKeyEnvironmentVariable];
        }

        var configuredProvider = section["Provider"]?.Trim();
        string provider;
        if (string.IsNullOrEmpty(configuredProvider))
        {
            provider = string.IsNullOrWhiteSpace(section["Smtp:Host"]) ? EmailProviders.None : EmailProviders.Smtp;
        }
        else
        {
            provider = EmailProviders.Normalize(configuredProvider)
                       ?? throw new InvalidOperationException(
                           $"Email:Provider inválido: \"{configuredProvider}\". Use Smtp, Resend ou None.");
        }

        if (provider == EmailProviders.Smtp && string.IsNullOrWhiteSpace(section["Smtp:Host"]))
        {
            throw new InvalidOperationException(
                "Email:Provider=Smtp, mas Email:Smtp:Host não está configurado. Defina Email__Smtp__Host " +
                "(ex.: mailpit no compose) ou use Email:Provider=None. Veja docs/email-e-recuperacao-de-senha.md.");
        }

        services.Configure<EmailOptions>(section);
        services.PostConfigure<EmailOptions>(options =>
        {
            options.Provider = provider;
            options.Resend.ApiKey = string.IsNullOrWhiteSpace(resendApiKey) ? null : resendApiKey;
        });

        // Chave do Resend salva pela tela: protegida com o Data Protection (anel de chaves fora do banco).
        services.AddDataProtectionKeyRing(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ResendApiKeyProtector>();
        services.TryAddSingleton<EmailSettingsResolver>();
        services.TryAddSingleton<TestEmailThrottle>();

        services.TryAddSingleton<SmtpEmailSender>();
        var resend = section.GetSection("Resend").Get<ResendOptions>() ?? new ResendOptions();
        services.AddHttpClient<ResendEmailSender>(client =>
        {
            client.BaseAddress = new Uri(resend.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, resend.TimeoutSeconds));
        });

        services.TryAddSingleton<IEmailSender, ConfiguredEmailSender>();
        services.TryAddScoped<IEmailSettingsService, EmailSettingsService>();

        return services;
    }

    /// <summary>
    /// Data Protection (cookies, antiforgery, tokens do Identity e a chave do Resend da tela, #47).
    /// Anel de chaves: <c>DataProtection:KeysPath</c> se configurado; senão o padrão do ASP.NET
    /// (<c>$HOME/.aspnet/DataProtection-Keys</c>, que no compose é o volume <c>web_keys</c>).
    /// Fica FORA do banco de propósito: o backup do banco sozinho não abre a chave do Resend.
    /// </summary>
    private static void AddDataProtectionKeyRing(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddDataProtection();
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            builder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }
    }

    private static void AddIdentityServices(this IServiceCollection services, IConfiguration configuration)
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

        // Recuperação de senha (#44): link de 1 h e uma vez só; sessões antigas caem em até 1 min.
        var timing = configuration.GetSection(IdentityTimingOptions.SectionName).Get<IdentityTimingOptions>()
                     ?? new IdentityTimingOptions();
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = timing.PasswordResetTokenLifespan);
        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = timing.SecurityStampValidationInterval);
        services.Configure<AppUrlOptions>(configuration.GetSection(AppUrlOptions.SectionName));
        services.AddSingleton<PasswordResetQueue>();
        services.AddScoped<PasswordResetService>();
        services.AddScoped<IPasswordResetService>(sp => sp.GetRequiredService<PasswordResetService>());
        services.AddHostedService<PasswordResetEmailWorker>();
    }
}
