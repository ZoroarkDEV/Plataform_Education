using System.ComponentModel.DataAnnotations;

namespace DevPath.Web.ViewModels.Account;

/// <summary>Campos do formulário de login (POST /Account/Login).</summary>
public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe sua senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Manter conectado")]
    public bool RememberMe { get; set; }

    /// <summary>Para onde voltar depois do login (ex.: /dashboard). Só URLs locais são aceitas.</summary>
    public string? ReturnUrl { get; set; }
}
