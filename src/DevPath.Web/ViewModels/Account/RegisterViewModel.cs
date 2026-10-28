using System.ComponentModel.DataAnnotations;

namespace DevPath.Web.ViewModels.Account;

/// <summary>Campos do formulário de cadastro (POST /Account/Register).</summary>
/// <remarks>
/// Data Annotations validam no servidor e, com o _ValidationScriptsPartial, também no navegador.
/// As regras de senha aqui espelham a política do Identity (8+ caracteres, número, minúscula, maiúscula).
/// </remarks>
public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Informe seu nome.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "O nome deve ter entre {2} e {1} caracteres.")]
    [Display(Name = "Nome")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(256)]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe uma senha.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "A senha deve ter pelo menos {2} caracteres.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$",
        ErrorMessage = "A senha precisa ter letra minúscula, letra maiúscula e número.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirme a senha.")]
    [Compare(nameof(Password), ErrorMessage = "As senhas não conferem.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmar senha")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
