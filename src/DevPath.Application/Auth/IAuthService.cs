namespace DevPath.Application.Auth;

/// <summary>
/// Casos de uso de autenticação. A Web só conhece esta interface; quem implementa
/// (com ASP.NET Identity) é a Infrastructure. Assim o controller fica fino e testável.
/// </summary>
public interface IAuthService
{
    /// <summary>Cria a conta e já autentica o usuário (cookie).</summary>
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>Valida e-mail/senha, conta tentativas para lockout e atualiza LastLoginAt.</summary>
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task LogoutAsync();
}
