namespace DevPath.Application.Auth;

/// <summary>Dados para criar uma conta. Já chegam validados pela camada Web (ViewModel).</summary>
public sealed record RegisterRequest(string Email, string Password, string DisplayName);

/// <summary>Dados para entrar. <paramref name="RememberMe"/> mantém o cookie após fechar o navegador.</summary>
public sealed record LoginRequest(string Email, string Password, bool RememberMe);

/// <summary>
/// Resultado de registro/login. Em vez de lançar exceção para erro "esperado"
/// (senha errada, e-mail repetido), devolvemos um resultado que o controller sabe exibir.
/// </summary>
public sealed record AuthResult
{
    public bool Succeeded { get; private init; }
    public bool IsLockedOut { get; private init; }
    public IReadOnlyList<string> Errors { get; private init; } = [];

    public static AuthResult Success() => new() { Succeeded = true };

    public static AuthResult Failure(params IEnumerable<string> errors) => new() { Errors = errors.ToList() };

    public static AuthResult LockedOut(string message) => new() { IsLockedOut = true, Errors = [message] };
}
