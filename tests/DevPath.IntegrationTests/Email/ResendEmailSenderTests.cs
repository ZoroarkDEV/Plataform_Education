using System.Net;
using System.Text.Json;
using DevPath.Application.Email;
using DevPath.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevPath.IntegrationTests.Email;

/// <summary><see cref="ResendEmailSender"/> com um HttpMessageHandler falso (nada sai para a internet).</summary>
public sealed class ResendEmailSenderTests
{
    private const string FakeKey = "re_teste_somente_em_memoria_123";
    private static readonly EmailMessage Message =
        new("ana@devpath.test", "DevPath: crie sua nova senha", "<p>Olá</p>", "Olá");

    [Fact]
    public async Task SendAsync_PostaEmApiEmailsComBearerEJsonCompleto()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"49a3999c-0ce1-4ea6-ab68-afcd6dc2e794"}""")
        });
        var (sender, _) = Create(handler);

        await sender.SendAsync(Message);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("https://api.resend.com/emails"));
        request.Authorization.Should().Be($"Bearer {FakeKey}");
        request.ContentType.Should().Be("application/json");

        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;
        root.GetProperty("from").GetString().Should().Be("DevPath <nao-responda@devpath.app>");
        root.GetProperty("to").EnumerateArray().Select(e => e.GetString()).Should().Equal("ana@devpath.test");
        root.GetProperty("subject").GetString().Should().Be("DevPath: crie sua nova senha");
        root.GetProperty("html").GetString().Should().Be("<p>Olá</p>");
        root.GetProperty("text").GetString().Should().Be("Olá");
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"statusCode":422,"name":"validation_error","message":"The domain is not verified."}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"statusCode":401,"name":"missing_api_key","message":"Missing API key"}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"statusCode":429,"name":"rate_limit_exceeded","message":"Too many requests"}""")]
    [InlineData(HttpStatusCode.InternalServerError, """{"statusCode":500,"name":"internal_server_error","message":"boom"}""")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    public async Task SendAsync_Resposta4xxOu5xx_LancaEmailDeliveryExceptionELogaSemAChave(HttpStatusCode status, string body)
    {
        var (sender, logs) = Create(new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) }));

        var act = () => sender.SendAsync(Message);

        (await act.Should().ThrowAsync<EmailDeliveryException>()).Which.Message.Should().Contain(((int)status).ToString());
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Text.Contains(((int)status).ToString()));
        logs.Entries.Should().NotContain(e => e.Text.Contains(FakeKey));
    }

    [Fact]
    public async Task SendAsync_Timeout_LancaEmailDeliveryException()
    {
        var (sender, logs) = Create(new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }), timeout: TimeSpan.FromMilliseconds(200));

        var act = () => sender.SendAsync(Message);

        await act.Should().ThrowAsync<EmailDeliveryException>().WithMessage("*Tempo esgotado*");
        logs.Entries.Should().NotContain(e => e.Text.Contains(FakeKey));
    }

    [Fact]
    public async Task SendAsync_FalhaDeRede_LancaEmailDeliveryException()
    {
        var (sender, _) = Create(new FakeHandler(_ => throw new HttpRequestException("Connection refused")));

        await ((Func<Task>)(() => sender.SendAsync(Message))).Should().ThrowAsync<EmailDeliveryException>();
    }

    private static (ResendEmailSender Sender, ListLoggerProvider Logs) Create(FakeHandler handler, TimeSpan? timeout = null)
    {
        var logs = new ListLoggerProvider();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.resend.com/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(15)
        };
        var options = Options.Create(new EmailOptions
        {
            FromAddress = "nao-responda@devpath.app",
            FromName = "DevPath",
            Resend = new ResendOptions { ApiKey = FakeKey }
        });
        return (new ResendEmailSender(client, options, new LoggerFactory([logs]).CreateLogger<ResendEmailSender>()), logs);
    }
}

/// <summary>Grava as requisições (com o corpo já lido) e responde o que o teste mandar.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _respond = (r, _) => Task.FromResult(respond(r));

    public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
        return await _respond(request, cancellationToken);
    }
}

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? ContentType, string Body);

/// <summary>Logger que guarda as mensagens formatadas (e a exceção) para os testes conferirem.</summary>
public sealed class ListLoggerProvider : ILoggerProvider
{
    public List<(LogLevel Level, string Text)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new ListLogger(this);

    public void Dispose()
    {
    }

    private sealed class ListLogger(ListLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (owner.Entries)
            {
                owner.Entries.Add((logLevel, formatter(state, exception) + " " + exception));
            }
        }
    }
}
