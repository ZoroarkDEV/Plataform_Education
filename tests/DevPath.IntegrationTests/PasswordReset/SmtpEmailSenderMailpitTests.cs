using System.Net.Http.Json;
using System.Text.Json;
using DevPath.Application.Email;
using DevPath.Infrastructure.Email;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;

namespace DevPath.IntegrationTests.PasswordReset;

/// <summary>
/// O <see cref="SmtpEmailSender"/> de verdade contra um Mailpit (mesma imagem do docker-compose),
/// num container de nome e portas próprios (portas aleatórias no host).
/// </summary>
public sealed class SmtpEmailSenderMailpitTests : IAsyncLifetime
{
    private const int SmtpPort = 1025;
    private const int ApiPort = 8025;

    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.27")
        .WithName($"devpath-test-mailpit-{Guid.NewGuid():N}")
        .WithPortBinding(SmtpPort, assignRandomHostPort: true)
        .WithPortBinding(ApiPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPort(ApiPort).ForPath("/api/v1/messages")))
        .Build();

    public Task InitializeAsync() => _mailpit.StartAsync();

    public async Task DisposeAsync() => await _mailpit.DisposeAsync();

    [Fact]
    public async Task SendAsync_EntregaNoMailpitComAssuntoTextoEHtml()
    {
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            FromAddress = "nao-responda@devpath.test",
            FromName = "DevPath",
            Smtp = new SmtpOptions { Host = _mailpit.Hostname, Port = _mailpit.GetMappedPublicPort(SmtpPort) }
        }));
        var link = "https://devpath.test/Account/ResetPassword?userId=1&token=abc";

        await sender.SendAsync(DevPath.Infrastructure.Identity.PasswordResetEmail.Build(
            "ana@devpath.test", "Ana", link, TimeSpan.FromHours(1)));

        using var http = new HttpClient
        {
            BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(ApiPort)}")
        };
        var list = await http.GetFromJsonAsync<JsonElement>("/api/v1/messages");
        var summary = list.GetProperty("messages")[0];
        summary.GetProperty("Subject").GetString().Should().Be("DevPath: crie sua nova senha");
        summary.GetProperty("To")[0].GetProperty("Address").GetString().Should().Be("ana@devpath.test");
        summary.GetProperty("From").GetProperty("Address").GetString().Should().Be("nao-responda@devpath.test");

        var message = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{summary.GetProperty("ID").GetString()}");
        message.GetProperty("Text").GetString().Should().Contain(link).And.Contain("1 hora");
        message.GetProperty("HTML").GetString().Should().Contain("Criar nova senha").And.Contain("lang=\"pt-BR\"");
    }
}
