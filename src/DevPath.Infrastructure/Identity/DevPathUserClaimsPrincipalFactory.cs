using System.Security.Claims;
using DevPath.Application.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DevPath.Infrastructure.Identity;

/// <summary>Adiciona o DisplayName como claim no cookie, para as views usarem sem consultar o banco.</summary>
internal sealed class DevPathUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(DevPathClaimTypes.DisplayName, user.DisplayName));
        return identity;
    }
}
