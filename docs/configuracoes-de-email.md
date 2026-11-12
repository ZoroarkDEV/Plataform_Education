# Configurações de e-mail pela tela de admin (#47)

O admin escolhe o provedor (`None`, `Smtp`, `Resend`), o remetente e a chave da API do Resend por uma página
da própria DevPath, em vez de editar variável de ambiente no servidor. O envio em si continua o do #44
([email-e-recuperacao-de-senha.md](email-e-recuperacao-de-senha.md)).

## Contrato (Application, `DevPath.Application.Email`)

```csharp
public interface IEmailSettingsService
{
    Task<EmailSettingsDto> GetAsync(CancellationToken ct = default);
    Task<EmailSettingsSaveResult> SaveAsync(UpdateEmailSettingsCommand command, Guid adminUserId, CancellationToken ct = default);
    Task<EmailTestResult> SendTestAsync(Guid adminUserId, CancellationToken ct = default);
}

public sealed record EmailSettingsDto(
    EmailProviderKind Provider, string FromAddress, string FromName,          // o que está salvo (ou a configuração, se nada salvo)
    bool ResendKeyConfigured, string? ResendKeyHint, EmailSecretSource ResendKeySource,
    bool RestartRequired, IReadOnlyList<string> MissingRequirements,
    DateTimeOffset? UpdatedAt, Guid? UpdatedByUserId, string? UpdatedByDisplayName,
    EmailSettingsSource EffectiveSource, bool LockedToConfiguration,
    EmailProviderKind EffectiveProvider, string EffectiveFromAddress, bool CanSend);

public sealed record UpdateEmailSettingsCommand(
    EmailProviderKind Provider, string? FromAddress, string? FromName,
    string? NewResendApiKey = null, bool ClearResendApiKey = false);

public sealed record EmailSettingsSaveResult(
    EmailSettingsSaveStatus Status,                                           // Saved | Invalid | Forbidden
    IReadOnlyDictionary<string, string[]> Errors,                             // chave = nome da propriedade do comando
    EmailSettingsDto? Settings) { public bool Succeeded { get; } }

public sealed record EmailTestResult(
    EmailTestStatus Status,                                                   // Sent | NotConfigured | Failed | RateLimited | Forbidden
    string Message, string? SentTo, EmailProviderKind Provider, TimeSpan? RetryAfter = null) { public bool Succeeded { get; } }

public enum EmailProviderKind { None, Smtp, Resend }
public enum EmailSettingsSource { Configuration, Database }
public enum EmailSecretSource { None, Database, Environment }
```

Registrado em `AddInfrastructure` (scoped). Não há endpoint novo no backend: a página é da Web
(controller de admin, mesmo padrão do `/admin/tentativas`: `[Authorize]`, policy `Admin` → senão 404,
`Cache-Control: no-store`, antiforgery nos POSTs). O serviço confere o papel `Admin` de novo em
`SaveAsync`/`SendTestAsync` (defesa em profundidade) e devolve `Forbidden`.

### Como a página usa

- **GET:** `GetAsync()`. Preenche o formulário com `Provider`, `FromAddress`, `FromName`. O campo da chave
  fica **sempre vazio**; ao lado, "Chave configurada (…`ResendKeyHint`)" quando `ResendKeyConfigured`.
  `ResendKeySource = Environment` = a chave vem do secret do servidor (a tela pode cadastrar outra por cima).
- **Avisos:** `MissingRequirements` (frases prontas em PT-BR) quando não vazio; `RestartRequired` = o que
  falta só se resolve no ambiente do servidor + reinício (ex.: `Smtp` sem `Email__Smtp__Host`);
  `LockedToConfiguration` = "Neste ambiente (dev/staging) o envio usa sempre o Mailpit; o que for salvo aqui
  só vale em produção". `UpdatedAt` + `UpdatedByDisplayName` = "Alterado por … em …".
- **POST salvar:** `SaveAsync(new UpdateEmailSettingsCommand(...), adminUserId)`. `NewResendApiKey` vazio
  **mantém** a chave salva; `ClearResendApiKey = true` remove (com chave nova junto = erro).
  `Errors` mapeia direto para o `ModelState` (`FromAddress`, `FromName`, `NewResendApiKey`, `Provider`; `""` = geral).
- **POST e-mail de teste (opcional):** `SendTestAsync(adminUserId)` envia "DevPath: e-mail de teste" para o
  e-mail do admin logado, com a configuração em vigor. 1 por admin por `Email:TestEmailCooldownSeconds` (60 s),
  em memória por instância. `Message` é pronta para a tela e nunca tem a chave.

## Validação

| Campo | Regra |
|---|---|
| `Provider` | `None`, `Smtp` ou `Resend` |
| `FromAddress` | obrigatório e e-mail válido (só o endereço, sem nome, sem espaço, domínio com ponto, até 254) quando o provedor não é `None` |
| `FromName` | opcional (vazio = `DevPath`), até 100 caracteres, sem quebra de linha nem `< > "` |
| `NewResendApiKey` | vazio = mantém; senão começa com `re_`, 10 a 200 caracteres, sem espaço. A mensagem de erro não ecoa o valor |
| `ClearResendApiKey` | não pode vir junto com uma chave nova |

Salvar `Resend` sem chave é permitido: fica salvo e `MissingRequirements` diz o que falta.

## Precedência: banco x configuração

| Situação | O envio usa |
|---|---|
| `Email:LockedToConfiguration=true` (Development e `docker-compose.yml`: **dev e staging**) | só a configuração (`Email:*`, Mailpit). O que a tela salva fica guardado, mas não vale |
| produção, nada salvo pela tela | a configuração (`Email:*` + `Email__Resend__ApiKey`/`RESEND_API_KEY`), como no #44 |
| produção, algo salvo pela tela | provedor, remetente e nome **salvos**. Chave: a salva; sem chave salva, a do ambiente |

O servidor SMTP (host, porta, TLS, usuário/senha) vem **sempre** da configuração; a tela só escolhe usar `Smtp`.

## Sem reiniciar

O `IEmailSender` é o `ConfiguredEmailSender`, que pergunta ao `EmailSettingsResolver` a cada envio. O
resolver guarda o resultado por `Email:SettingsCacheSeconds` (30 s) **por instância**; salvar limpa o cache
na hora nesta instância (com várias instâncias, as outras pegam em até 30 s). Nada do que a tela salva pede
reinício ou rebuild. Só pedem reinício as mudanças no ambiente do servidor (ex.: `Email__Smtp__Host`), e a
tela sabe por `RestartRequired`.

**Startup:** `Resend` sem chave **sobe** (antes derrubava). Continuam falhando no startup só os erros do
ambiente: provider desconhecido em `Email:Provider` e `Smtp` sem `Email:Smtp:Host`. Se o banco estiver fora
do ar, o envio usa a configuração do servidor (aviso no log) e tenta o banco de novo em 5 s.

## Onde a chave fica e como é protegida

- Tabela `EmailSettings` (uma linha, `Id = 1`, migration `20261112144223_AddEmailSettings`): `Provider`,
  `FromAddress`, `FromName`, `ResendApiKeyProtected`, `UpdatedAt`, `UpdatedByUserId` (FK para `AspNetUsers`,
  `SET NULL` se a conta for apagada). CHECKs: uma linha só e provedor conhecido.
- `ResendApiKeyProtected` = saída do **ASP.NET Data Protection** (`IDataProtector` com propósito
  `DevPath.Email.ResendApiKey.v1`; AES-256-CBC + HMACSHA256, chaves com rotação automática). O texto puro
  nunca vai para o banco, appsettings, repositório, log, DTO ou HTML.
- **Anel de chaves do Data Protection: fora do banco, de propósito** (um dump/backup do banco sozinho não abre
  a chave). Padrão do ASP.NET: `$HOME/.aspnet/DataProtection-Keys`, que no container é
  `/home/app/.aspnet/DataProtection-Keys`, montado no volume **`web_keys`** do compose (já existia para
  cookies/antiforgery; sobrevive a recriar o container). Outro lugar: `DataProtection__KeysPath=/caminho`
  (precisa ser persistente e gravável pelo usuário `app`).
- **Produção sem compose:** monte um volume persistente em `/home/app/.aspnet/DataProtection-Keys` (ou
  defina `DataProtection__KeysPath`). Sem isso, as chaves somem a cada deploy: os logins caem e a chave do
  Resend salva fica ilegível. Nesse caso a tela mostra "A chave do Resend salva não pôde ser lida… Cadastre a
  chave de novo" e o envio para com essa mensagem (não cai para outra chave em silêncio).
- **Backup:** guarde o volume `web_keys` junto (e separado) do backup do banco. Perdeu as chaves = cadastre a
  chave do Resend de novo na tela.
- **Log/auditoria:** salvar registra `Configuração de e-mail salva pelo admin {UserId}: provedor {Provider},
  chave do Resend {trocada|mantida|removida|ausente}`, sem o valor. `UpdatedAt`/`UpdatedByUserId` ficam na tabela.

## Configuração nova

| Chave (variável) | Padrão | Uso |
|---|---|---|
| `Email:LockedToConfiguration` (`Email__LockedToConfiguration`) | `false`; Development e compose: `true` | Ignora a tela e usa só `Email:*` (dev/staging no Mailpit) |
| `Email:SettingsCacheSeconds` | `30` | Cache por instância da configuração salva |
| `Email:TestEmailCooldownSeconds` | `60` | Intervalo mínimo entre e-mails de teste do mesmo admin |
| `DataProtection:KeysPath` (`DataProtection__KeysPath`) | vazio (= `$HOME/.aspnet/DataProtection-Keys`) | Pasta persistente do anel de chaves |

## Testes

- `tests/DevPath.UnitTests/Infrastructure/EmailSettingsResolverTests.cs`: precedência, trava de dev/staging,
  o que falta, chave ilegível, `ToString` sem chave, validação de endereço.
- `tests/DevPath.IntegrationTests/Email/EmailSettingsServiceTests.cs` (banco real, Resend falso): Web sobe com
  Resend sem chave; chave mascarada e protegida no banco (a coluna não contém a chave); vale no próximo envio
  sem reiniciar (cache de 5 min no teste); manter/remover a chave; validação; só admin; e-mail de teste com
  limite e erro explicado; recuperação de senha do #44 usando a chave da tela; nenhum log com a chave.
- `EmailProviderSelectionTests` (startup) e `MigrationsTests` (`AddEmailSettings` é a última, CHECKs e `Down`).
