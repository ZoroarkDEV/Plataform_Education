using System.Security.Claims;

namespace DevPath.Web.Onboarding;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Id do usuário logado (claim NameIdentifier que o Identity grava no cookie).</summary>
    public static bool TryGetUserId(this ClaimsPrincipal user, out Guid userId) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
