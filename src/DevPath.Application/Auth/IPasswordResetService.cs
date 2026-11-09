namespace DevPath.Application.Auth;

/// <summary>Dados para trocar a senha pelo link do e-mail. Já chegam validados pela Web (ViewModel).</summary>
/// <param name="UserId">Id do usuário que veio no link.</param>
/// <param name="Token">Token do link, como veio na URL (Base64Url).</param>
public sealed record ResetPasswordRequest(string UserId, string Token, string NewPassword);

/// <summary>Resultado da troca de senha.</summary>
public sealed record ResetPasswordResult
{
    public bool Succeeded { get; private init; }

    /// <summary>Link vencido, já usado, adulterado ou de usuário que não existe: a tela pede um link novo.</summary>
    public bool InvalidLink { get; private init; }

    /// <summary>Mensagens em português (ex.: a regra de senha do cadastro).</summary>
    public IReadOnlyList<string> Errors { get; private init; } = [];

    public static ResetPasswordResult Success() => new() { Succeeded = true };

    public static ResetPasswordResult LinkNoLongerValid() => new() { InvalidLink = true };

    public static ResetPasswordResult Failure(params IEnumerable<string> errors) => new() { Errors = errors.ToList() };
}

/// <summary>
/// Recuperação de senha (US-AUTH.1, issue #44), com os tokens do ASP.NET Identity:
/// expiram (<c>Identity:PasswordResetTokenLifespan</c>, 1 h) e valem uma vez só (o token carrega o
/// security stamp, que muda quando a senha é trocada).
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Registra o pedido e volta na hora, exista a conta ou não. A busca do usuário, o token e o envio
    /// do e-mail acontecem em segundo plano: a resposta não revela, nem pelo tempo, se o e-mail tem conta.
    /// </summary>
    ValueTask RequestResetAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>O link ainda vale? (Para a tela de nova senha já mostrar "Este link não vale mais".)</summary>
    Task<bool> IsLinkValidAsync(string userId, string token, CancellationToken cancellationToken = default);

    /// <summary>Troca a senha, atualiza o security stamp (encerra as outras sessões) e invalida o link.</summary>
    Task<ResetPasswordResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
