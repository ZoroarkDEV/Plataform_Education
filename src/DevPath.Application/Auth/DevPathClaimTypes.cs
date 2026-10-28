namespace DevPath.Application.Auth;

/// <summary>Claims extras que colocamos no cookie de autenticação.</summary>
public static class DevPathClaimTypes
{
    /// <summary>Nome de exibição do usuário (evita ir ao banco só para mostrar "Olá, Fulano").</summary>
    public const string DisplayName = "devpath:display_name";
}
