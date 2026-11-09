# E-mail e recuperação de senha (#44)

Fluxo (US-AUTH.1): **Entrar → "Esqueci minha senha" → e-mail com link → nova senha → Entrar**.

## Como funciona

| Etapa | Rota | Observações |
|---|---|---|
| Pedir o link | `GET/POST /Account/ForgotPassword` | Antiforgery. Sempre redireciona para `/Account/ForgotPasswordConfirmation` com "Se este e-mail tiver uma conta, enviamos um link…", exista a conta ou não. |
| Nova senha | `GET/POST /Account/ResetPassword?userId=…&token=…` | Antiforgery. Mesmas regras e mensagens do cadastro. Link vencido/usado/adulterado: "Este link não vale mais. Peça um novo." |
| Sucesso | redireciona para `/Account/Login` | Mensagem "Senha alterada. Você já pode entrar." |

- **Tokens do Identity** (`GeneratePasswordResetTokenAsync` / `ResetPasswordAsync`). Expiram em
  `Identity:PasswordResetTokenLifespan` (1 h; vale para todos os tokens do `DataProtectorTokenProvider`).
  **Uma vez só:** o token carrega o security stamp, e a troca de senha gera um stamp novo, então o
  mesmo link (e qualquer link pedido antes) deixa de valer.
- **Outras sessões caem:** o stamp novo derruba os outros cookies de login na próxima checagem do
  `SecurityStampValidator` (`Identity:SecurityStampValidationInterval`, 1 min; o padrão do Identity seria 30 min).
  O bloqueio por senhas erradas (lockout) também é liberado, pois a pessoa provou ter acesso ao e-mail.
- **Sem enumeração (ADR-005):** o POST só põe o e-mail numa fila em memória (`PasswordResetQueue`) e
  responde. A busca da conta, o token e o SMTP rodam em segundo plano (`PasswordResetEmailWorker`), então a
  resposta e o tempo são iguais para e-mail com e sem conta. Um pedido na fila se perde se a Web reiniciar.
- **Rate limit** (`RateLimiting:PasswordReset`, em memória por instância): por e-mail, 3 por hora (passou
  do limite: mesma resposta, sem e-mail); por IP, 20 por hora (passou: 429 com "Você fez muitos pedidos
  seguidos…"). Atrás de proxy, o IP vem do `X-Forwarded-For` só de proxies confiáveis (`ForwardedHeaders:*`).
- **Link absoluto** a partir de `App:PublicBaseUrl`, **nunca** do header `Host` (que o cliente controla).
  Sem essa chave, o e-mail não é enviado (erro no log). A página do link manda `Referrer-Policy: no-referrer`
  e `Cache-Control: no-store`. Nem o link nem o e-mail do aluno vão para o log.

## Quem envia: `Email:Provider`

O envio fica atrás de `IEmailSender` (`DevPath.Application.Email`). A implementação sai da configuração:

| `Email:Provider` | Implementação | Onde |
|---|---|---|
| `Smtp` | `SmtpEmailSender` (MailKit) | Development (`appsettings.Development.json`) e o compose (dev e staging): Mailpit |
| `Resend` | `ResendEmailSender` (`POST https://api.resend.com/emails`, HttpClient tipado) | **Produção** (decisão do dono, 2026-11-09) |
| `None` | só registra um aviso no log, não envia | padrão do `appsettings.json` (testes, produção antes da chave) |
| vazio | `Smtp` se `Email:Smtp:Host` existir, senão `None` | compatibilidade |

Configuração incompleta **derruba o startup** com mensagem clara: `Resend` sem chave, `Smtp` sem
`Email:Smtp:Host` ou um provider desconhecido. Uma recusa do provedor (4xx/5xx, timeout, rede) é
registrada no log (status e resposta do Resend, nunca a chave) e o worker segue para o próximo e-mail;
o aluno vê a mesma tela e pode pedir de novo.

## Configuração

| Chave (variável de ambiente) | Padrão | Uso |
|---|---|---|
| `Email:Provider` (`Email__Provider`) | `None`; Development e compose: `Smtp` | `Smtp`, `Resend` ou `None` |
| `Email:Resend:ApiKey` (`Email__Resend__ApiKey` ou `RESEND_API_KEY`) | **nunca no repositório** | Chave da API do Resend, só por secret |
| `Email:Resend:BaseUrl` | `https://api.resend.com` | |
| `Email:Resend:TimeoutSeconds` | `15` | Timeout do HttpClient |
| `App:PublicBaseUrl` (`App__PublicBaseUrl`) | vazio; Development: `http://localhost:5055`; compose: `${PUBLIC_BASE_URL:-http://localhost:8080}` | Base dos links dos e-mails |
| `Email:FromAddress` / `Email:FromName` | `nao-responda@devpath.local` / `DevPath` | Remetente |
| `Email:Smtp:Host` | vazio (= não envia, só avisa no log); Development: `localhost`; compose: `mailpit` | Servidor SMTP |
| `Email:Smtp:Port` | `1025` | |
| `Email:Smtp:Security` | `None` | `None`, `Auto`, `StartTls` (587), `SslOnConnect` (465) |
| `Email:Smtp:Username` / `Email:Smtp:Password` | vazio (sem autenticação) | Senha só por variável de ambiente/segredo |
| `Email:Smtp:TimeoutSeconds` | `15` | |
| `Identity:PasswordResetTokenLifespan` | `01:00:00` | Validade do link |
| `Identity:SecurityStampValidationInterval` | `00:01:00` | Em quanto tempo as outras sessões caem |
| `RateLimiting:PasswordReset:PerEmailPermitLimit` / `PerEmailWindowMinutes` | `3` / `60` | Limite por e-mail |
| `RateLimiting:PasswordReset:PerIpPermitLimit` / `PerIpWindowMinutes` | `20` / `60` | Limite por IP |
| `RateLimiting:PasswordReset:SegmentsPerWindow` | `6` | Janela deslizante |

## Dev e staging: Mailpit

O `docker-compose.yml` tem o serviço `mailpit` (`axllent/mailpit`), uma caixa de e-mail local que **não
entrega nada de verdade**. A Web envia para `mailpit:1025` pela rede interna do compose.

- Interface: **http://localhost:8025** (`MAILPIT_UI_PORT`). SMTP no host: `localhost:1025`
  (`MAILPIT_SMTP_PORT`), usado pelo `dotnet run` em Development.
- As portas ficam presas a `127.0.0.1` porque a interface não tem login.
- Nome do container: `devpath-mailpit` (`MAILPIT_CONTAINER_NAME`). Para um staging no mesmo host, use outro
  nome e outras portas no `.env` do staging (ex.: `devpath-staging-mailpit`, `1026`, `8026`) e
  `PUBLIC_BASE_URL` com o endereço do staging.
- Só a infraestrutura, para `dotnet run`: `docker compose up -d postgres redis mailpit`.

Para testar: abra `/Account/Login` → "Esqueci minha senha", informe o e-mail de uma conta e abra o e-mail
"DevPath: crie sua nova senha" na interface do Mailpit.

## Produção: Resend

Decisão do dono (#44, 2026-11-09): produção envia pelo **Resend**; dev e staging continuam no Mailpit.

1. **Domínio verificado.** No painel do Resend (Domains), adicione o domínio público do DevPath e crie
   os registros DNS que ele pede (SPF e DKIM; DMARC recomendado). Enquanto o domínio não estiver
   *Verified*, o Resend recusa o envio com 403/422 e o log mostra a mensagem dele.
2. **Remetente.** `Email__FromAddress` precisa ser um endereço desse domínio verificado
   (ex.: `nao-responda@<domínio>`). `Email__FromName` = `DevPath`.
3. **Chave.** Crie uma API key com permissão **Sending access**, restrita ao domínio. **O dono envia a
   chave por um canal seguro** (gerenciador de segredos/cofre, nunca chat, issue ou e-mail) e ela entra só
   no secret do deploy, como `Email__Resend__ApiKey` (ou `RESEND_API_KEY`). Ela nunca vai para o
   repositório, para o `appsettings*.json` nem para o `.env.example` (um teste garante que os appsettings
   versionados não têm valor em `ApiKey`). Para trocar a chave, gere outra no Resend, atualize o secret,
   reinicie a Web e revogue a antiga.
4. **Variáveis da Web em produção:**

```
Email__Provider=Resend
Email__Resend__ApiKey=<secret do deploy>
Email__FromAddress=nao-responda@<domínio verificado>
Email__FromName=DevPath
App__PublicBaseUrl=https://<domínio público>
```

Sem a chave, a Web **não sobe** (mensagem "Email:Provider=Resend, mas a chave do Resend não foi
informada…"), para o problema aparecer no deploy e não no primeiro aluno que esqueceu a senha.
**Não convide pessoas reais antes de configurar.** O texto do e-mail é o mesmo em todos os provedores:
PT-BR, assunto "DevPath: crie sua nova senha", HTML + texto.
