using Microsoft.AspNetCore.Identity;

namespace DevPath.Infrastructure.Identity;

/// <summary>
/// Mensagens de erro do Identity em português, para quem está começando (o padrão vem em inglês).
/// Todos os métodos são sobrescritos para nunca aparecer inglês na tela.
/// Os <see cref="IdentityError.Code"/> continuam os mesmos do Identity (não muda a API).
/// Nenhuma mensagem repete o e-mail/usuário digitado (#33, ADR-005).
/// </summary>
public sealed class PortugueseIdentityErrorDescriber : IdentityErrorDescriber
{
    /// <summary>Mesma frase da dica de senha do cadastro (RegisterViewModel.PasswordRule).</summary>
    public const string PasswordRule =
        "A senha precisa ter pelo menos 8 caracteres, com letra maiúscula, letra minúscula e número.";

    public const string EmailAlreadyRegistered = "Já existe uma conta com este e-mail.";
    public const string InvalidEmailMessage = "E-mail inválido.";
    public const string GenericError = "Não foi possível concluir a operação. Confira os dados e tente de novo.";

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };

    public override IdentityError DefaultError() => Error(nameof(DefaultError), GenericError);

    public override IdentityError ConcurrencyFailure() =>
        Error(nameof(ConcurrencyFailure), "Seus dados foram alterados em outro lugar enquanto você editava. Recarregue a página e tente de novo.");

    public override IdentityError PasswordMismatch() =>
        Error(nameof(PasswordMismatch), "Senha incorreta.");

    public override IdentityError InvalidToken() =>
        Error(nameof(InvalidToken), "Este link ou código não é mais válido. Peça um novo e tente de novo.");

    public override IdentityError RecoveryCodeRedemptionFailed() =>
        Error(nameof(RecoveryCodeRedemptionFailed), "Não foi possível usar este código de recuperação. Confira o código e tente de novo.");

    public override IdentityError LoginAlreadyAssociated() =>
        Error(nameof(LoginAlreadyAssociated), "Este login externo já está ligado a outra conta.");

    // Usamos o e-mail como UserName, então as mensagens de usuário falam de e-mail.
    public override IdentityError InvalidUserName(string? userName) =>
        Error(nameof(InvalidUserName), InvalidEmailMessage);

    public override IdentityError InvalidEmail(string? email) =>
        Error(nameof(InvalidEmail), InvalidEmailMessage);

    public override IdentityError DuplicateUserName(string userName) =>
        Error(nameof(DuplicateUserName), EmailAlreadyRegistered);

    public override IdentityError DuplicateEmail(string email) =>
        Error(nameof(DuplicateEmail), EmailAlreadyRegistered);

    public override IdentityError InvalidRoleName(string? role) =>
        Error(nameof(InvalidRoleName), "Nome de perfil inválido.");

    public override IdentityError DuplicateRoleName(string role) =>
        Error(nameof(DuplicateRoleName), "Já existe um perfil com este nome.");

    public override IdentityError UserAlreadyHasPassword() =>
        Error(nameof(UserAlreadyHasPassword), "Esta conta já tem uma senha.");

    public override IdentityError UserLockoutNotEnabled() =>
        Error(nameof(UserLockoutNotEnabled), "O bloqueio de acesso não está ativado para esta conta.");

    public override IdentityError UserAlreadyInRole(string role) =>
        Error(nameof(UserAlreadyInRole), "Esta conta já tem este perfil.");

    public override IdentityError UserNotInRole(string role) =>
        Error(nameof(UserNotInRole), "Esta conta não tem este perfil.");

    // Regras de senha: o aluno vê sempre a mesma frase da dica (o AuthService remove repetidas).
    public override IdentityError PasswordTooShort(int length) =>
        Error(nameof(PasswordTooShort),
            $"A senha precisa ter pelo menos {length} caracteres, com letra maiúscula, letra minúscula e número.");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), PasswordRule);

    // A política não exige símbolo (RequireNonAlphanumeric = false); se vier, não pedimos símbolo.
    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), PasswordRule);

    public override IdentityError PasswordRequiresDigit() =>
        Error(nameof(PasswordRequiresDigit), PasswordRule);

    public override IdentityError PasswordRequiresLower() =>
        Error(nameof(PasswordRequiresLower), PasswordRule);

    public override IdentityError PasswordRequiresUpper() =>
        Error(nameof(PasswordRequiresUpper), PasswordRule);
}
