using DevPath.Application.Email;
using FluentAssertions;
using DevPath.Infrastructure.Email;
using Microsoft.AspNetCore.DataProtection;

namespace DevPath.UnitTests.Infrastructure;

/// <summary>#47: precedência configuração x tela, trava de dev/staging e o que falta para enviar.</summary>
public sealed class EmailSettingsResolverTests
{
    private const string EnvKey = "re_do_ambiente_1234567890";
    private const string DbKey = "re_da_tela_abcdefghijWXYZ";
    private static readonly ResendApiKeyProtector Protector = new(new EphemeralDataProtectionProvider());

    private static EmailOptions Config(string provider = "Resend", string? envKey = null, string? smtpHost = null, bool locked = false) => new()
    {
        Provider = provider,
        FromAddress = "config@devpath.app",
        FromName = "Config",
        LockedToConfiguration = locked,
        Smtp = new SmtpOptions { Host = smtpHost },
        Resend = new ResendOptions { ApiKey = envKey }
    };

    private static EmailSettingsRecord Stored(string provider = "Resend", string? key = DbKey, string from = "tela@devpath.app") => new()
    {
        Provider = provider,
        FromAddress = from,
        FromName = "Tela",
        ResendApiKeyProtected = key is null ? null : Protector.Protect(key),
        UpdatedAt = DateTimeOffset.UnixEpoch,
        UpdatedByUserId = Guid.NewGuid()
    };

    [Fact]
    public void SemNadaSalvo_UsaAConfiguracao()
    {
        var s = EmailSettingsResolver.Resolve(Config(envKey: EnvKey), null, Protector);

        s.Source.Should().Be(EmailSettingsSource.Configuration);
        s.Provider.Should().Be("Resend");
        s.FromAddress.Should().Be("config@devpath.app");
        s.ResendApiKey.Should().Be(EnvKey);
        s.ConfiguredResendKeySource.Should().Be(EmailSecretSource.Environment);
        s.CanSend.Should().BeTrue();
        s.Stored.Should().BeNull();
    }

    [Fact]
    public void SalvoNaTela_VencePorCimaDaConfiguracao_ChaveDaTelaVenceADoAmbiente()
    {
        var s = EmailSettingsResolver.Resolve(Config("Smtp", envKey: EnvKey, smtpHost: "mailpit"), Stored(), Protector);

        s.Source.Should().Be(EmailSettingsSource.Database);
        s.Provider.Should().Be("Resend");
        s.FromAddress.Should().Be("tela@devpath.app");
        s.FromName.Should().Be("Tela");
        s.ResendApiKey.Should().Be(DbKey);
        s.ConfiguredResendKeySource.Should().Be(EmailSecretSource.Database);
        s.CanSend.Should().BeTrue();
    }

    [Fact]
    public void SalvoSemChave_UsaAChaveDoAmbiente()
    {
        var s = EmailSettingsResolver.Resolve(Config(envKey: EnvKey), Stored(key: null), Protector);

        s.ResendApiKey.Should().Be(EnvKey);
        s.ConfiguredResendKeySource.Should().Be(EmailSecretSource.Environment);
        s.CanSend.Should().BeTrue();
    }

    [Fact]
    public void AmbienteTravado_DevEStaging_IgnoraATelaEFicaNoMailpit()
    {
        var s = EmailSettingsResolver.Resolve(Config("Smtp", smtpHost: "mailpit", locked: true), Stored(), Protector);

        s.Source.Should().Be(EmailSettingsSource.Configuration);
        s.LockedToConfiguration.Should().BeTrue();
        s.Provider.Should().Be("Smtp");
        s.FromAddress.Should().Be("config@devpath.app");
        s.ResendApiKey.Should().BeNull("Smtp não usa chave");
        s.Stored.Should().NotBeNull("a tela ainda mostra o que foi salvo");
        s.CanSend.Should().BeTrue();
    }

    [Fact]
    public void ResendSemChave_NaoQuebra_DizOQueFalta()
    {
        var s = EmailSettingsResolver.Resolve(Config(), null, Protector);

        s.CanSend.Should().BeFalse();
        s.RestartRequired.Should().BeFalse("a chave se cadastra na tela, sem reiniciar");
        s.MissingRequirements.Should().ContainSingle().Which.Should().Contain("chave da API do Resend");
    }

    [Fact]
    public void ChaveSalvaIlegivel_AnelDeChavesTrocado_PedeParaCadastrarDeNovo()
    {
        var record = Stored();
        var otherRing = new ResendApiKeyProtector(new EphemeralDataProtectionProvider());

        var s = EmailSettingsResolver.Resolve(Config(envKey: EnvKey), record, otherRing);

        s.StoredKeyUnreadable.Should().BeTrue();
        s.ResendApiKey.Should().BeNull("não cai silenciosamente para outra chave");
        s.CanSend.Should().BeFalse();
        s.MissingRequirements.Should().ContainSingle().Which.Should().Contain("não pôde ser lida");
    }

    [Fact]
    public void SmtpSemHost_PedeConfiguracaoDoServidorEReinicio()
    {
        var s = EmailSettingsResolver.Resolve(Config(), Stored(provider: "Smtp"), Protector);

        s.CanSend.Should().BeFalse();
        s.RestartRequired.Should().BeTrue();
        s.MissingRequirements.Should().ContainSingle().Which.Should().Contain("Email__Smtp__Host");
    }

    [Fact]
    public void None_NaoEnvia()
    {
        var s = EmailSettingsResolver.Resolve(Config("None"), null, Protector);

        s.CanSend.Should().BeFalse();
        s.MissingRequirements.Should().ContainSingle().Which.Should().Contain("None");
    }

    [Fact]
    public void RemetenteInvalido_Falta()
    {
        var s = EmailSettingsResolver.Resolve(Config(envKey: EnvKey), Stored(from: "sem-arroba"), Protector);

        s.CanSend.Should().BeFalse();
        s.MissingRequirements.Should().Contain(m => m.Contains("FromAddress"));
    }

    [Fact]
    public void ToString_NuncaMostraAChave()
    {
        var s = EmailSettingsResolver.Resolve(Config(envKey: EnvKey), Stored(), Protector);

        s.ToString().Should().NotContain(DbKey).And.NotContain(EnvKey);
        new UpdateEmailSettingsCommand(EmailProviderKind.Resend, "a@b.co", "X", DbKey).ToString().Should().NotContain(DbKey);
    }

    [Theory]
    [InlineData("nao-responda@devpath.app", true)]
    [InlineData("a@b.co", true)]
    [InlineData("", false)]
    [InlineData("sem-arroba", false)]
    [InlineData("Nome <a@b.co>", false)]
    [InlineData("a@b.co\r\nBcc: x@y.z", false)]
    [InlineData("a b@c.co", false)]
    [InlineData("a@localhost", false)]
    public void IsValidEmailAddress(string value, bool expected) =>
        EmailSettingsResolver.IsValidEmailAddress(value).Should().Be(expected);
}
