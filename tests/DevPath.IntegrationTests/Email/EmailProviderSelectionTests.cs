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
    [InlineData("Smtp", "mailpit", null, "Smtp")]
    [InlineData("smtp", "mailpit", null, "Smtp")]
    [InlineData("Resend", null, "re_teste", "Resend")]
    [InlineData("None", "mailpit", "re_teste", "None")]
    [InlineData(null, "mailpit", null, "Smtp")]
    [InlineData(null, null, null, "None")]
    public async Task Provider_EscolheOProvedorDoEnvio(string? provider, string? smtpHost, string? resendKey, string expected)
    {
        var services = Build(new()
        {
            ["Email:Provider"] = provider,
            ["Email:Smtp:Host"] = smtpHost,
            ["Email:Resend:ApiKey"] = resendKey
        });

        // #47: um IEmailSender só, que resolve o provedor a cada envio (configuração + tela de admin).
        services.GetRequiredService<IEmailSender>().Should().BeOfType<ConfiguredEmailSender>();
        (await services.GetRequiredService<EmailSettingsResolver>().GetAsync()).Provider.Should().Be(expected);
    }

    [Fact]
    public void Resend_ChaveDeRESEND_API_KEY_TambemVale()
    {
        var services = Build(new() { ["Email:Provider"] = "Resend", ["RESEND_API_KEY"] = "re_da_variavel" });

        services.GetRequiredService<IOptions<EmailOptions>>().Value.Resend.ApiKey.Should().Be("re_da_variavel");
    }

    [Fact]
    public void Resend_HttpClientTipado_ApontaParaApiResendComTimeout()
    {
        var services = Build(new() { ["Email:Provider"] = "Resend", ["Email:Resend:ApiKey"] = "re_x", ["Email:Resend:TimeoutSeconds"] = "7" });
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ResendEmailSender));

        client.BaseAddress.Should().Be(new Uri("https://api.resend.com/"));
        client.Timeout.Should().Be(TimeSpan.FromSeconds(7));
        client.DefaultRequestHeaders.Authorization.Should().BeNull("a chave vai só na requisição, nunca fixa no client");
    }

    [Theory]
    [InlineData("Smtp", null, null, "Email__Smtp__Host")]
    [InlineData("SendGrid", null, null, "Use Smtp, Resend ou None")]
    public void ConfiguracaoIncompleta_FalhaNoRegistroComMensagemClara(string provider, string? smtpHost, string? key, string hint)
    {
        var act = () => Build(new() { ["Email:Provider"] = provider, ["Email:Smtp:Host"] = smtpHost, ["Email:Resend:ApiKey"] = key });

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{hint}*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task ResendSemChave_RegistraSemFalhar_EOEnvioDizOQueFalta(string? key)
    {
        // #47: antes derrubava o startup; agora a Web sobe e o envio explica o motivo.
        var services = Build(new() { ["Email:Provider"] = "Resend", ["Email:Resend:ApiKey"] = key });

        var act = () => services.GetRequiredService<IEmailSender>()
            .SendAsync(new EmailMessage("ana@devpath.test", "Assunto", "<p>x</p>", "x"));

        (await act.Should().ThrowAsync<EmailDeliveryException>()).Which.Message.Should().Contain("chave da API do Resend");
    }

    [Fact]
    public async Task Startup_ResendSemChave_AppSobe()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:DevPath", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1");
            builder.UseSetting("Email:Provider", "Resend");
            builder.UseSetting("Email:Resend:ApiKey", "");
            builder.UseSetting("RESEND_API_KEY", "");
        });

        var act = () => factory.CreateClient();

        act.Should().NotThrow();
        var settings = await factory.Services.GetRequiredService<EmailSettingsResolver>().GetAsync();
        settings.Provider.Should().Be("Resend");
        settings.CanSend.Should().BeFalse();
        settings.MissingRequirements.Should().Contain(m => m.Contains("chave da API do Resend"));
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
