using DevPath.Application.Auth;
using Microsoft.AspNetCore.Identity;

namespace DevPath.Infrastructure.Identity;

/// <summary>Implementação de <see cref="IAuthService"/> com UserManager/SignInManager do Identity.</summary>
internal sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : IAuthService
{
    // Mesma mensagem para "e-mail não existe" e "senha errada": não revelamos quais e-mails têm conta.
    internal const string InvalidCredentialsMessage = "E-mail ou senha inválidos.";
    internal const string LockedOutMessage = "Conta bloqueada temporariamente por excesso de tentativas. Tente novamente em alguns minutos.";

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

        // CreateAsync valida a política de senha, e-mail único, e grava o hash (nunca a senha).
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return AuthResult.Failure(result.Errors.Select(e => e.Description));
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return AuthResult.Success();
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return AuthResult.Failure(InvalidCredentialsMessage);
        }

        // lockoutOnFailure: true -> cada senha errada conta para o bloqueio (5 tentativas / 15 min).
        var result = await signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return AuthResult.LockedOut(LockedOutMessage);
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
}
