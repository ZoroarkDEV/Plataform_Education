using DevPath.Application.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace DevPath.Infrastructure.Identity;

/// <summary>Implementação de <see cref="IAuthService"/> com UserManager/SignInManager do Identity.</summary>
/// <remarks>
/// Proteção contra enumeração de contas no login (ver ADR-005):
/// <list type="bullet">
/// <item>Uma única mensagem para e-mail inexistente, senha errada, conta bloqueada e login não permitido.</item>
/// <item>Tempo parecido nos dois caminhos: quando o e-mail não existe, verificamos a senha contra um
/// hash "falso" (mesmo custo do PBKDF2 de um usuário real). Sem isso, a resposta para e-mail
/// inexistente vinha em ~3 ms contra ~55 ms, o que revelava quais e-mails têm conta.</item>
/// </list>
/// </remarks>
internal sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IPasswordHasher<ApplicationUser> passwordHasher,
    ILogger<AuthService> logger) : IAuthService
{
    internal const string InvalidCredentialsMessage = "E-mail ou senha inválidos.";

    // Usuário e hash usados só para "gastar" o mesmo tempo de uma verificação real.
    // O hash é gerado uma vez por processo, com o mesmo IPasswordHasher (mesmo algoritmo e iterações).
    private static readonly ApplicationUser DummyUser = new() { UserName = "dummy@devpath.invalid" };
    private static string? _dummyHash;

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow // o registro já autentica, então conta como login
        };

        // CreateAsync valida a política de senha e o e-mail único, e grava o hash (nunca a senha).
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return AuthResult.Failure(DescribeErrors(result.Errors));
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return AuthResult.Success();
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            SpendPasswordHashTime(request.Password);
            return AuthResult.Failure(InvalidCredentialsMessage);
        }

        // lockoutOnFailure: true -> cada senha errada conta para o bloqueio (5 tentativas / 15 min).
        var result = await signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut || result.IsNotAllowed)
        {
            // O Identity checa bloqueio ANTES da senha, então esse caminho também seria rápido demais.
            SpendPasswordHashTime(request.Password);

            // O aluno vê a mensagem genérica; o motivo real fica só no log do servidor.
            logger.LogWarning("Login recusado para o usuário {UserId}: {Reason}.",
                user.Id, result.IsLockedOut ? "conta bloqueada" : "login não permitido");

            return result.IsLockedOut
                ? AuthResult.LockedOut(InvalidCredentialsMessage)
                : AuthResult.Failure(InvalidCredentialsMessage);
        }

        if (!result.Succeeded)
        {
            return AuthResult.Failure(InvalidCredentialsMessage);
        }

        user.LastLoginAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        return AuthResult.Success();
    }

    public Task LogoutAsync() => signInManager.SignOutAsync();

    private void SpendPasswordHashTime(string password)
    {
        // Corrida inofensiva: no pior caso duas threads geram o hash uma vez cada.
        _dummyHash ??= passwordHasher.HashPassword(DummyUser, "DevPath-dummy-password-1");
        passwordHasher.VerifyHashedPassword(DummyUser, _dummyHash, password);
    }

    // UserName = Email, então e-mail repetido gera DuplicateEmail E DuplicateUserName.
    // Mostramos só uma vez.
    private static IEnumerable<string> DescribeErrors(IEnumerable<IdentityError> errors)
    {
        var list = errors.ToList();
        if (list.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateEmail)))
        {
            list.RemoveAll(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName));
        }

        return list.Select(e => e.Description).Distinct();
    }
}
