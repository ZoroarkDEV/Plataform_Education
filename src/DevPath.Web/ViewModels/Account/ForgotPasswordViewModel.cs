using System.ComponentModel.DataAnnotations;

namespace DevPath.Web.ViewModels.Account;

/// <summary>Formulário "Esqueci minha senha" (POST /Account/ForgotPassword).</summary>
public sealed class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(256, ErrorMessage = "O e-mail deve ter no máximo {1} caracteres.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;
}
