using Microsoft.AspNetCore.Identity;

namespace DevPath.Infrastructure.Identity;

/// <summary>
/// Usuário da plataforma (SPEC §4.1 "User"), estendendo o usuário do ASP.NET Identity.
/// </summary>
/// <remarks>
/// Fica na Infrastructure, e não no Domain, porque herda de <see cref="IdentityUser{TKey}"/>:
/// colocá-lo no Domain obrigaria o Domain a depender do pacote do Identity.
/// O Domain só enxerga o usuário pelo <c>Guid UserId</c>.
/// Email, PasswordHash, lockout etc. já vêm do IdentityUser.
/// </remarks>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public const string DefaultLanguage = "pt-BR";

    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public string PreferredLanguage { get; set; } = DefaultLanguage;

    /// <summary>Trilha que o aluno está seguindo agora (FK para CareerPaths, opcional).</summary>
    public Guid? CurrentPathId { get; set; }

    public bool IsAdmin { get; set; }
}
