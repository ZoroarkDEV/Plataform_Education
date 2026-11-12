using System.Net;
using System.Text.Json;
using DevPath.Application.Email;
using DevPath.Infrastructure.Email;
using DevPath.Infrastructure.Persistence;
using DevPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevPath.IntegrationTests.Email;

/// <summary>
/// Tela de admin das configurações de e-mail (#47), com banco real: salvar/ler mascarado, chave protegida no banco,
/// vale sem reiniciar, validação, auditoria, só admin, e-mail de teste e nada de chave em log ou DTO.
/// </summary>
[Collection(EmailSettingsCollection.Name)]
public sealed class EmailSettingsServiceTests(EmailSettingsWebApplicationFactory factory)
{
    /// <summary>Toda chave usada aqui começa assim: nenhum log pode conter este trecho.</summary>
    private const string SecretMarker = "re_SEGREDO_";

    private static string NewKey() => SecretMarker + Guid.NewGuid().ToString("N");

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private async Task<T> WithServiceAsync<T>(Func<IEmailSettingsService, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IEmailSettingsService>());
    }

    private Task<EmailSettingsSaveResult> SaveAsync(UpdateEmailSettingsCommand command, Guid adminId) =>
        WithServiceAsync(s => s.SaveAsync(command, adminId));

    private Task<EmailSettingsDto> GetAsync() => WithServiceAsync(s => s.GetAsync());

    private async Task SendAsync(string to)
    {
        var sender = factory.Services.GetRequiredService<IEmailSender>();
        await sender.SendAsync(new EmailMessage(to, "Assunto", "<p>Oi</p>", "Oi"));
    }

    private async Task<string> RawRowAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        return await db.Database
            .SqlQueryRaw<string>("SELECT row_to_json(e)::text AS \"Value\" FROM \"EmailSettings\" e")
            .SingleAsync();
    }

    [Fact]
    public async Task Startup_ResendSemChave_WebSobe_StatusEEnvioDizemOQueFalta()
    {
        // Estado da produção de hoje: Resend no ambiente, sem chave, nada salvo pela tela.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
            await db.EmailSettings.ExecuteDeleteAsync();
        }

        factory.Services.GetRequiredService<EmailSettingsResolver>().Invalidate();

        var health = await factory.CreateBrowserClient().GetAsync("/health");
        health.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await GetAsync();
        dto.EffectiveSource.Should().Be(EmailSettingsSource.Configuration);
        dto.EffectiveProvider.Should().Be(EmailProviderKind.Resend);
        dto.ResendKeyConfigured.Should().BeFalse();
        dto.ResendKeySource.Should().Be(EmailSecretSource.None);
        dto.CanSend.Should().BeFalse();
        dto.RestartRequired.Should().BeFalse();
        dto.UpdatedAt.Should().BeNull();
        dto.MissingRequirements.Should().ContainSingle().Which.Should().Contain("Cadastre a chave nesta tela");

        var act = () => SendAsync("ana@devpath.test");
        (await act.Should().ThrowAsync<EmailDeliveryException>()).Which.Message.Should().Contain("chave da API do Resend");
    }

    [Fact]
    public async Task Salvar_ChaveVoltaSoMascarada_EFicaProtegidaNoBanco()
    {
        var (adminId, _) = await factory.CreateUserAsync(admin: true, displayName: "Dona Admin");
        var key = NewKey();

        var result = await SaveAsync(new UpdateEmailSettingsCommand(
            EmailProviderKind.Resend, " nao-responda@devpath.test ", " DevPath ", key), adminId);

        result.Status.Should().Be(EmailSettingsSaveStatus.Saved);
        var dto = result.Settings!;
        dto.Provider.Should().Be(EmailProviderKind.Resend);
        dto.FromAddress.Should().Be("nao-responda@devpath.test");
        dto.FromName.Should().Be("DevPath");
        dto.ResendKeyConfigured.Should().BeTrue();
        dto.ResendKeyHint.Should().Be(key[^4..]);
        dto.ResendKeySource.Should().Be(EmailSecretSource.Database);
        dto.EffectiveSource.Should().Be(EmailSettingsSource.Database);
        dto.CanSend.Should().BeTrue();
        dto.MissingRequirements.Should().BeEmpty();

        // Auditoria: quem e quando, sem segredo.
        dto.UpdatedByUserId.Should().Be(adminId);
        dto.UpdatedByDisplayName.Should().Be("Dona Admin");
        dto.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        // Nenhum DTO devolve a chave inteira (nem o Get, nem o resultado do salvar).
        Json(result).Should().NotContain(key).And.NotContain(SecretMarker);
        Json(await GetAsync()).Should().NotContain(key).And.NotContain(SecretMarker);

        // Em repouso: a coluna tem só o texto do Data Protection.
        var raw = await RawRowAsync();
        raw.Should().NotContain(key).And.NotContain(SecretMarker).And.NotContain(key[SecretMarker.Length..]);
        raw.Should().Contain(adminId.ToString());
        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<DevPathDbContext>().EmailSettings.AsNoTracking().SingleAsync();
        stored.ResendApiKeyProtected.Should().NotBeNullOrEmpty();
        factory.Services.GetRequiredService<ResendApiKeyProtector>().TryUnprotect(stored.ResendApiKeyProtected!).Should().Be(key);
    }

    [Fact]
    public async Task Salvar_ValeNoProximoEnvio_SemReiniciar_MesmoComCache()
    {
        var (adminId, _) = await factory.CreateUserAsync(admin: true);
        var first = NewKey();
        var second = NewKey();

        (await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "um@devpath.test", "Um", first), adminId)).Succeeded.Should().BeTrue();
        var to1 = $"r1-{Guid.NewGuid():N}@devpath.test";
        await SendAsync(to1);
        var request1 = factory.Resend.To(to1).Should().ContainSingle().Subject;
        request1.Authorization.Should().Be($"Bearer {first}");
        request1.Body.Should().Contain("Um \\u003Cum@devpath.test\\u003E");

        // Cache de 5 min na factory: só passa se o salvar invalidar.
        (await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "dois@devpath.test", "Dois", second), adminId)).Succeeded.Should().BeTrue();
        var to2 = $"r2-{Guid.NewGuid():N}@devpath.test";
        await SendAsync(to2);
        var request2 = factory.Resend.To(to2).Should().ContainSingle().Subject;
        request2.Authorization.Should().Be($"Bearer {second}");
        request2.Body.Should().Contain("dois@devpath.test");

        // Trocar para None também vale na hora: nada vai ao Resend.
        (await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.None, null, null), adminId)).Succeeded.Should().BeTrue();
        var to3 = $"r3-{Guid.NewGuid():N}@devpath.test";
        await SendAsync(to3);
        factory.Resend.To(to3).Should().BeEmpty();
    }

    [Fact]
    public async Task Salvar_SemChaveNova_MantemAChave_ELimparRemove()
    {
        var (adminId, _) = await factory.CreateUserAsync(admin: true);
        var key = NewKey();
        await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "a@devpath.test", "A", key), adminId);

        var kept = await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "b@devpath.test", "B", NewResendApiKey: "  "), adminId);
        kept.Settings!.ResendKeyConfigured.Should().BeTrue();
        kept.Settings.ResendKeyHint.Should().Be(key[^4..]);
        kept.Settings.FromAddress.Should().Be("b@devpath.test");

        var cleared = await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "b@devpath.test", "B", ClearResendApiKey: true), adminId);
        cleared.Status.Should().Be(EmailSettingsSaveStatus.Saved);
        cleared.Settings!.ResendKeyConfigured.Should().BeFalse();
        cleared.Settings.ResendKeyHint.Should().BeNull();
        cleared.Settings.CanSend.Should().BeFalse();
        cleared.Settings.MissingRequirements.Should().ContainSingle().Which.Should().Contain("chave da API do Resend");
        (await RawRowAsync()).Should().Contain("\"ResendApiKeyProtected\":null");
    }

    [Theory]
    [InlineData(EmailProviderKind.Resend, "", "DevPath", null, false, "FromAddress")]
    [InlineData(EmailProviderKind.Smtp, "sem-arroba", "DevPath", null, false, "FromAddress")]
    [InlineData(EmailProviderKind.Resend, "Nome <a@devpath.test>", "DevPath", null, false, "FromAddress")]
    [InlineData(EmailProviderKind.Resend, "a@devpath.test", "Linha\r\nBcc: x@y.z", null, false, "FromName")]
    [InlineData(EmailProviderKind.Resend, "a@devpath.test", "DevPath", "sk_live_nao_e_do_resend_123", false, "NewResendApiKey")]
    [InlineData(EmailProviderKind.Resend, "a@devpath.test", "DevPath", "re_com espaco_123456", false, "NewResendApiKey")]
    [InlineData(EmailProviderKind.Resend, "a@devpath.test", "DevPath", "re_SEGREDO_e_limpar_juntos", true, "NewResendApiKey")]
    [InlineData((EmailProviderKind)42, "a@devpath.test", "DevPath", null, false, "Provider")]
    public async Task Salvar_Invalido_NaoGrava_EDizOCampo(
        EmailProviderKind provider, string from, string name, string? key, bool clear, string field)
    {
        var (adminId, _) = await factory.CreateUserAsync(admin: true);
        var before = await RawRowOrNullAsync();

        var result = await SaveAsync(new UpdateEmailSettingsCommand(provider, from, name, key, clear), adminId);

        result.Status.Should().Be(EmailSettingsSaveStatus.Invalid);
        result.Settings.Should().BeNull();
        result.Errors.Should().ContainKey(field);
        if (key is not null)
        {
            Json(result).Should().NotContain(key, "a mensagem de erro não ecoa a chave digitada");
        }

        (await RawRowOrNullAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Salvar_NoneSemRemetente_Vale()
    {
        var (adminId, _) = await factory.CreateUserAsync(admin: true);

        var result = await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.None, "", ""), adminId);

        result.Status.Should().Be(EmailSettingsSaveStatus.Saved);
        result.Settings!.FromName.Should().Be("DevPath");
        result.Settings.CanSend.Should().BeFalse();
    }

    [Fact]
    public async Task NaoAdmin_NaoSalvaNemEnviaTeste()
    {
        var (studentId, _) = await factory.CreateUserAsync(admin: false);
        var before = await RawRowOrNullAsync();

        var save = await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "x@devpath.test", "X", NewKey()), studentId);
        var test = await WithServiceAsync(s => s.SendTestAsync(studentId));
        var unknown = await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.None, "", ""), Guid.NewGuid());

        save.Status.Should().Be(EmailSettingsSaveStatus.Forbidden);
        unknown.Status.Should().Be(EmailSettingsSaveStatus.Forbidden);
        test.Status.Should().Be(EmailTestStatus.Forbidden);
        (await RawRowOrNullAsync()).Should().Be(before);
    }

    [Fact]
    public async Task EmailDeTeste_VaiParaOAdmin_ComLimite_EFalhaExplicada()
    {
        var (adminId, adminEmail) = await factory.CreateUserAsync(admin: true);
        var key = NewKey();
        await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "nao-responda@devpath.test", "DevPath", key), adminId);

        var sent = await WithServiceAsync(s => s.SendTestAsync(adminId));

        sent.Status.Should().Be(EmailTestStatus.Sent);
        sent.SentTo.Should().Be(adminEmail);
        sent.Provider.Should().Be(EmailProviderKind.Resend);
        var request = factory.Resend.To(adminEmail).Should().ContainSingle().Subject;
        request.Authorization.Should().Be($"Bearer {key}");
        request.Body.Should().Contain(EmailSettingsService.TestEmailSubject);

        var again = await WithServiceAsync(s => s.SendTestAsync(adminId));
        again.Status.Should().Be(EmailTestStatus.RateLimited);
        again.RetryAfter.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
        factory.Resend.To(adminEmail).Should().HaveCount(1);

        // Outro admin, provedor recusando (domínio não verificado): Failed com explicação, sem a chave.
        var (otherId, otherEmail) = await factory.CreateUserAsync(admin: true);
        factory.Resend.Status = HttpStatusCode.UnprocessableEntity;
        try
        {
            var failed = await WithServiceAsync(s => s.SendTestAsync(otherId));
            failed.Status.Should().Be(EmailTestStatus.Failed);
            failed.Message.Should().Contain("domínio verificado");
            Json(failed).Should().NotContain(key);
            factory.Resend.To(otherEmail).Should().HaveCount(1);
        }
        finally
        {
            factory.Resend.Status = HttpStatusCode.OK;
        }
    }

    [Fact]
    public async Task EmailDeTeste_SemChave_NaoTentaEnviar()
    {
        var (adminId, adminEmail) = await factory.CreateUserAsync(admin: true);
        await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "a@devpath.test", "A", ClearResendApiKey: true), adminId);

        var result = await WithServiceAsync(s => s.SendTestAsync(adminId));

        result.Status.Should().Be(EmailTestStatus.NotConfigured);
        result.Message.Should().Contain("chave da API do Resend");
        factory.Resend.To(adminEmail).Should().BeEmpty();
    }

    [Fact]
    public async Task RecuperacaoDeSenha_44_UsaAConfiguracaoSalvaNaTela()
    {
        var (adminId, _) = await factory.CreateUserAsync(admin: true);
        var (_, studentEmail) = await factory.CreateUserAsync(admin: false, displayName: "Aluna");
        var key = NewKey();
        await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "nao-responda@devpath.test", "DevPath", key), adminId);

        var client = factory.CreateBrowserClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.47.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}");
        var response = await client.SubmitFormAsync("/Account/ForgotPassword", "/Account/ForgotPassword",
            new Dictionary<string, string> { ["Email"] = studentEmail });
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var request = await factory.Resend.WaitForAsync(studentEmail);
        request.Authorization.Should().Be($"Bearer {key}");
        request.Body.Should().Contain("DevPath: crie sua nova senha").And.Contain("https://devpath.test/Account/ResetPassword");
    }

    [Fact]
    public async Task NenhumLog_TemAChave_DepoisDeSalvarLerEnviarEFalhar()
    {
        var (adminId, adminEmail) = await factory.CreateUserAsync(admin: true);
        var key = NewKey();
        await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "nao-responda@devpath.test", "DevPath", key), adminId);
        await GetAsync();
        await SaveAsync(new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "invalido", "DevPath", key + "x"), adminId);
        await WithServiceAsync(s => s.SendTestAsync(adminId));
        factory.Resend.Status = HttpStatusCode.Unauthorized;
        try
        {
            var to = $"falha-{Guid.NewGuid():N}@devpath.test";
            await ((Func<Task>)(() => SendAsync(to))).Should().ThrowAsync<EmailDeliveryException>();
        }
        finally
        {
            factory.Resend.Status = HttpStatusCode.OK;
        }

        factory.Resend.To(adminEmail).Should().NotBeEmpty("o envio de fato usou a chave");
        List<(LogLevel Level, string Text)> entries;
        lock (factory.Logs.Entries)
        {
            entries = [.. factory.Logs.Entries];
        }

        entries.Should().NotBeEmpty();
        entries.Should().Contain(e => e.Text.Contains($"Configuração de e-mail salva pelo admin {adminId}") && e.Text.Contains("trocada"));
        entries.Should().NotContain(e => e.Text.Contains(SecretMarker), "nenhuma chave (nem parte dela) vai para o log");
        var keyBody = key[SecretMarker.Length..];
        entries.Should().NotContain(e => e.Text.Contains(keyBody));
    }

    private async Task<string?> RawRowOrNullAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevPathDbContext>();
        return await db.Database
            .SqlQueryRaw<string>("SELECT row_to_json(e)::text AS \"Value\" FROM \"EmailSettings\" e")
            .SingleOrDefaultAsync();
    }
}
