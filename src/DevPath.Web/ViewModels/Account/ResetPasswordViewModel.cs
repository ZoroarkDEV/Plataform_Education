using System.ComponentModel.DataAnnotations;

namespace DevPath.Web.ViewModels.Account;

/// <summary>Formulário de nova senha (POST /Account/ResetPassword). Mesmas regras do cadastro.</summary>
public sealed class ResetPasswordViewModel
{
    /// <summary>Do link do e-mail (campo oculto).</summary>
    [Required(ErrorMessage = "Link incompleto.")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Do link do e-mail (campo oculto, Base64Url).</summary>
    [Required(ErrorMessage = "Link incompleto.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha nova.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = RegisterViewModel.PasswordRule)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", ErrorMessage = RegisterViewModel.PasswordRule)]
    [DataType(DataType.Password)]
    [Display(Name = "Senha nova")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirme a senha nova.")]
    [Compare(nameof(Password), ErrorMessage = "As duas senhas não são iguais.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmar senha nova")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
