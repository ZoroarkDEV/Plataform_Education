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

## Configuração

| Chave (variável de ambiente) | Padrão | Uso |
|---|---|---|
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

## Produção: pendente (decisão do dono)

O provedor de envio de produção ainda **não foi escolhido** (decisão pendente nº 7 do roteiro do PO).
Enquanto isso, em produção `Email:Smtp:Host` fica vazio: o "Esqueci a senha" responde normalmente, mas
nenhum e-mail sai (aviso "E-mail … NÃO enviado" no log). **Não convide pessoas reais antes de configurar.**

Quando o dono decidir, se o provedor aceitar SMTP (ex.: Amazon SES, SendGrid, Mailgun, Postmark, Brevo),
basta configurar por variável de ambiente, sem mudar código:

```
App__PublicBaseUrl=https://<domínio público>
Email__FromAddress=nao-responda@<domínio verificado no provedor>
Email__Smtp__Host=<host SMTP do provedor>
Email__Smtp__Port=587
Email__Smtp__Security=StartTls
Email__Smtp__Username=<usuário>
Email__Smtp__Password=<segredo>   # só no gerenciador de segredos do deploy
```

Também é preciso configurar SPF/DKIM do domínio no provedor. Um provedor só por API HTTP pede uma nova
implementação de `IEmailSender` (`DevPath.Application.Email`).
