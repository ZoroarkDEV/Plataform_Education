using DevPath.Application.Email;
using DevPath.Infrastructure;
using DevPath.Infrastructure.Email;
using DevPath.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevPath.IntegrationTests.Email;

/// <summary><c>Email:Provider</c> escolhe o <see cref="IEmailSender"/>; configuração incompleta falha no startup.</summary>
public sealed class EmailProviderSelectionTests
{
    [Theory]
    [InlineData("Smtp", "mailpit", null, typeof(SmtpEmailSender))]
    [InlineData("smtp", "mailpit", null, typeof(SmtpEmailSender))]
    [InlineData("Resend", null, "re_teste", typeof(ResendEmailSender))]
    [InlineData("None", "mailpit", "re_teste", null)]
    [InlineData(null, "mailpit", null, typeof(SmtpEmailSender))]
    [InlineData(null, null, null, null)]
    public void Provider_EscolheAImplementacao(string? provider, string? smtpHost, string? resendKey, Type? expected)
    {
        var sender = Build(new()
        {
            ["Email:Provider"] = provider,
            ["Email:Smtp:Host"] = smtpHost,
            ["Email:Resend:ApiKey"] = resendKey
        }).GetRequiredService<IEmailSender>();

        if (expected is null)
        {
            sender.GetType().Name.Should().Be("UnconfiguredEmailSender");
        }
        else
        {
            sender.Should().BeOfType(expected);
        }
    }

    [Fact]
    public void Resend_ChaveDeRESEND_API_KEY_TambemVale()
    {
        var services = Build(new() { ["Email:Provider"] = "Resend", ["RESEND_API_KEY"] = "re_da_variavel" });

        services.GetRequiredService<IEmailSender>().Should().BeOfType<ResendEmailSender>();
        services.GetRequiredService<IOptions<EmailOptions>>().Value.Resend.ApiKey.Should().Be("re_da_variavel");
    }

    [Fact]
    public void Resend_HttpClientTipado_ApontaParaApiResendComTimeout()
    {
        var services = Build(new() { ["Email:Provider"] = "Resend", ["Email:Resend:ApiKey"] = "re_x", ["Email:Resend:TimeoutSeconds"] = "7" });
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IEmailSender));

        client.BaseAddress.Should().Be(new Uri("https://api.resend.com/"));
        client.Timeout.Should().Be(TimeSpan.FromSeconds(7));
        client.DefaultRequestHeaders.Authorization.Should().BeNull("a chave vai só na requisição, nunca fixa no client");
    }

    [Theory]
    [InlineData("Resend", null, null, "Email__Resend__ApiKey")]
    [InlineData("Resend", null, "   ", "RESEND_API_KEY")]
    [InlineData("Smtp", null, null, "Email__Smtp__Host")]
    [InlineData("SendGrid", null, null, "Use Smtp, Resend ou None")]
    public void ConfiguracaoIncompleta_FalhaNoRegistroComMensagemClara(string provider, string? smtpHost, string? key, string hint)
    {
        var act = () => Build(new() { ["Email:Provider"] = provider, ["Email:Smtp:Host"] = smtpHost, ["Email:Resend:ApiKey"] = key });

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{hint}*");
    }

    [Fact]
    public async Task Startup_ResendSemChave_AppNaoSobe()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:DevPath", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x");
            builder.UseSetting("Email:Provider", "Resend");
            builder.UseSetting("Email:Resend:ApiKey", "");
            builder.UseSetting("RESEND_API_KEY", "");
        });

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Email:Provider=Resend, mas a chave do Resend não foi informada*");
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Where(kv => kv.Value is not null))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmailSender(configuration);
        return services.BuildServiceProvider();
    }
}
