using DevPath.Application.Email;
using DevPath.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevPath.IntegrationTests.PasswordReset;

/// <summary>
/// App inteira (Testing) com e-mail capturado, URL pública fixa, security stamp conferido em toda
/// requisição e X-Forwarded-For aceito (cada teste usa o próprio IP no rate limit).
/// </summary>
public sealed class PasswordResetWebApplicationFactory : DevPathWebApplicationFactory
{
    public const string PublicBaseUrl = "https://devpath.test";
    public const int PerIpLimit = 5;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("App:PublicBaseUrl", PublicBaseUrl);
        builder.UseSetting("Identity:SecurityStampValidationInterval", "00:00:00");
        builder.UseSetting("ForwardedHeaders:TrustAllProxies", "true");
        builder.UseSetting("RateLimiting:PasswordReset:PerIpPermitLimit", PerIpLimit.ToString());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<CapturingEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());
        });
    }

    public CapturingEmailSender Emails => Services.GetRequiredService<CapturingEmailSender>();

    /// <summary>Cliente de navegador com IP próprio (X-Forwarded-For) para não dividir o limite por IP.</summary>
    public HttpClient CreateClientFromNewIp() => WithNewIp(CreateBrowserClient());

    public static HttpClient WithNewIp(HttpClient client)
    {
        var bytes = Guid.NewGuid().ToByteArray();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{bytes[0]}.{bytes[1]}.{bytes[2]}");
        return client;
    }
}

[CollectionDefinition(Name)]
public sealed class PasswordResetCollection : ICollectionFixture<PasswordResetWebApplicationFactory>
{
    public const string Name = "PasswordReset";
}
