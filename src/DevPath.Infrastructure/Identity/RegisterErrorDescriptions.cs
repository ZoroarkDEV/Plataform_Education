using Microsoft.AspNetCore.Identity;

namespace DevPath.Infrastructure.Identity;

/// <summary>
/// Mensagens do CreateAsync na tela de cadastro. Códigos de senha viram a mesma frase da dica;
/// o restante (DefaultError, etc.) ganha um fallback específico de criar conta — não o texto
/// genérico do login.
/// </summary>
public static class RegisterErrorDescriptions
{
    public const string FailedToCreateAccount =
        "Não foi possível criar a conta. Confira os dados e tente de novo.";

    public static IEnumerable<string> Describe(IEnumerable<IdentityError> errors)
    {
        var list = errors.ToList();
        if (list.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateEmail)))
        {
            list.RemoveAll(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName));
        }

        return list.Select(Map).Distinct();
    }

    internal static string Map(IdentityError error)
    {
        if (error.Code.StartsWith("Password", StringComparison.Ordinal))
        {
            return PortugueseIdentityErrorDescriber.PasswordRule;
        }

        if (error.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
            or nameof(IdentityErrorDescriber.DuplicateUserName))
        {
            return error.Description;
        }

        return FailedToCreateAccount;
    }
}
