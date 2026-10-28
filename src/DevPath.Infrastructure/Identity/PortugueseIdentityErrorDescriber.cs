using Microsoft.AspNetCore.Identity;

namespace DevPath.Infrastructure.Identity;

/// <summary>Mensagens de erro do Identity em português (o padrão vem em inglês).</summary>
internal sealed class PortugueseIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = $"Já existe uma conta com o e-mail '{email}'." };

    // Usamos o e-mail como UserName, então a mensagem é a mesma.
    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = $"Já existe uma conta com o e-mail '{userName}'." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = $"O e-mail '{email}' é inválido." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = $"O e-mail '{userName}' contém caracteres inválidos." };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"A senha precisa ter pelo menos {length} caracteres." };

    public override IdentityError PasswordRequiresDigit() =>
        new() { Code = nameof(PasswordRequiresDigit), Description = "A senha precisa ter pelo menos um número (0-9)." };

    public override IdentityError PasswordRequiresLower() =>
        new() { Code = nameof(PasswordRequiresLower), Description = "A senha precisa ter pelo menos uma letra minúscula (a-z)." };

    public override IdentityError PasswordRequiresUpper() =>
        new() { Code = nameof(PasswordRequiresUpper), Description = "A senha precisa ter pelo menos uma letra maiúscula (A-Z)." };

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "A senha precisa ter pelo menos um símbolo." };
}
