# ADR-005 — Enumeração de contas (login e cadastro)

Status: Proposed
Data: 2026-10-28

## Contexto

"Enumeração de contas" é quando alguém de fora consegue descobrir **quais e-mails têm conta** no
DevPath. Isso facilita ataques direcionados: phishing, credential stuffing e tentativa de senha em
contas que já se sabe que existem.

A revisão de QA da issue #9 encontrou dois vazamentos:

1. **Login por tempo de resposta:** para um e-mail inexistente a resposta vinha em ~3 ms; para uma
   conta real, em ~55 ms, porque só a conta real passava pela verificação PBKDF2 da senha. A mensagem
   já era a mesma, mas o tempo entregava a resposta. O bloqueio por tentativas também tinha mensagem
   própria ("Conta bloqueada..."), que confirmava que a conta existe.
2. **Cadastro:** ao tentar criar conta com um e-mail já usado, a tela mostra
   "Já existe uma conta com o e-mail ...".

## Decisão

**Login: corrigido agora.**
- Uma única mensagem, "E-mail ou senha inválidos.", para e-mail inexistente, senha errada, conta
  bloqueada e login não permitido. O motivo real (ex.: bloqueio) vai só para o log do servidor.
- Quando o e-mail não existe, ou quando o Identity recusa antes de olhar a senha (conta bloqueada ou
  não permitida), o `AuthService` verifica a senha contra um hash falso, gerado uma vez com o mesmo
  `IPasswordHasher`. Assim os dois caminhos gastam o mesmo PBKDF2.
- As tentativas erradas em contas reais continuam contando para o bloqueio (5 tentativas / 15 min).

**Cadastro: aceito no MVP (trade-off consciente).** O cadastro continua informando que o e-mail já
existe. Esconder isso exige um fluxo de confirmação por e-mail: a tela diria sempre "enviamos um
e-mail para concluir o cadastro" e só o dono da caixa saberia se a conta já existia. O MVP não tem
envio de e-mail (`RequireConfirmedEmail = false`) e é de uso pessoal/pequeno. Para quem está
aprendendo, a mensagem clara vale mais do que o risco neste momento.

## Consequências

- **Mais fácil:** o login não revela contas nem pela mensagem nem por diferença grosseira de tempo.
  O teste de integração `Login_EmailInexistente_LevaTempoParecidoComSenhaErrada` é propositalmente
  frouxo: exige só que o caminho "não existe" leve pelo menos 30% do caminho "senha errada".
- **Mais difícil:** o usuário bloqueado não sabe que está bloqueado; vê "e-mail ou senha inválidos"
  até o bloqueio expirar. O suporte precisa olhar o log.
- **Risco aceito:** enumeração pelo cadastro, e também pelo tempo do cadastro.
- **Mitigações futuras (quando o DevPath tiver usuários externos):**
  - rate limiting por IP em `/Account/Login` e `/Account/Register` (o middleware de rate limiting
    do ASP.NET Core também é usado no sandbox, issue #13);
  - fluxo de confirmação de e-mail com mensagem genérica no cadastro e na recuperação de senha;
  - CAPTCHA ou desafio progressivo depois de N falhas por IP;
  - alertas no log para muitas falhas de login vindas do mesmo IP.
