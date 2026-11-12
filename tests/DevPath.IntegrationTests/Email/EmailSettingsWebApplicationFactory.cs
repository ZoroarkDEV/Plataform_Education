using System.Collections.Concurrent;
using System.Net;
using DevPath.Application.Analytics;
using DevPath.Infrastructure.Email;
using DevPath.Infrastructure.Identity;
using DevPath.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevPath.IntegrationTests.Email;

/// <summary>
/// App inteira (Testing, banco do Testcontainers) como a PRODUÇÃO de hoje: <c>Email:Provider=Resend</c> e SEM chave
/// no ambiente (#47: precisa subir). O Resend é um handler falso (nada sai para a internet) e todos os logs são guardados.
/// Cache da configuração longo (5 min) de propósito: prova que salvar invalida na hora.
/// </summary>
public sealed class EmailSettingsWebApplicationFactory : DevPathWebApplicationFactory
{
    public const string PublicBaseUrl = "https://devpath.test";
    public const string Password = "Senha123";

    public ListLoggerProvider Logs { get; } = new();

    public FakeResendApi Resend { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Email:Provider", "Resend");
        builder.UseSetting("Email:Resend:ApiKey", "");
        builder.UseSetting("RESEND_API_KEY", "");
        builder.UseSetting("Email:FromAddress", "config@devpath.test");
        builder.UseSetting("Email:SettingsCacheSeconds", "300");
        builder.UseSetting("Email:TestEmailCooldownSeconds", "60");
        builder.UseSetting("App:PublicBaseUrl", PublicBaseUrl);
        builder.UseSetting("ForwardedHeaders:TrustAllProxies", "true");
        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(Logs);
            logging.AddFilter<ListLoggerProvider>(null, LogLevel.Trace);
        });
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<ResendEmailSender>().ConfigurePrimaryHttpMessageHandler(() => new FakeResendHandler(Resend)));
    }

    public async Task<(Guid Id, string Email)> CreateUserAsync(bool admin, string displayName = "Admin Teste")
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"email-cfg-{Guid.NewGuid():N}@devpath.test";
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = displayName };
        (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        if (admin)
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync(AdminAccess.Role))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(AdminAccess.Role));
            }

            (await users.AddToRoleAsync(user, AdminAccess.Role)).Succeeded.Should().BeTrue();
        }

        return (user.Id, email);
    }
}

[CollectionDefinition(Name)]
public sealed class EmailSettingsCollection : ICollectionFixture<EmailSettingsWebApplicationFactory>
{
    public const string Name = "EmailSettings";
}

/// <summary>API do Resend de mentira: guarda as requisições e responde o status configurado.</summary>
public sealed class FakeResendApi
{
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>Próximas respostas (padrão 200).</summary>
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public IReadOnlyList<RecordedRequest> To(string email) =>
        Requests.Where(r => r.Body.Contains(email, StringComparison.OrdinalIgnoreCase)).ToList();

    public async Task<RecordedRequest> WaitForAsync(string email, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (To(email) is [.., var last])
            {
                return last;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Nenhuma chamada ao Resend para {email}.");
    }
}

/// <summary>Um handler por HttpClient (o IHttpClientFactory descarta handlers), todos gravando no mesmo <see cref="FakeResendApi"/>.</summary>
internal sealed class FakeResendHandler(FakeResendApi api) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        api.Requests.Enqueue(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
        var status = api.Status;
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(status == HttpStatusCode.OK
                ? """{"id":"00000000-0000-0000-0000-000000000001"}"""
                : $$"""{"statusCode":{{(int)status}},"name":"validation_error","message":"The domain is not verified."}""")
        };
    }
}
