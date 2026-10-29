using System.ComponentModel.DataAnnotations;

namespace DevPath.Web.ViewModels.Account;

/// <summary>Campos do formulário de cadastro (POST /Account/Register).</summary>
/// <remarks>
/// Data Annotations validam no SERVIDOR (ModelState), sempre. O DevPath não usa jquery-validation
/// (a stack é Tailwind + HTMX; o _ValidationScriptsPartial é vazio de propósito). No navegador, a
/// validação vem dos atributos HTML5 que a view coloca nos inputs (required, minlength, maxlength e
/// o type="email" que o [EmailAddress] gera). Ao mudar um limite aqui, ajuste também a view.
/// Toda regra tem ErrorMessage em português (há um teste que garante isso).
/// As regras de senha aqui espelham a política do Identity (8+ caracteres, número, minúscula, maiúscula).
/// </remarks>
public sealed class RegisterViewModel
{
    /// <summary>Regra de senha (US-11.1): aparece como dica ao lado do campo e como mensagem de erro.</summary>
    public const string PasswordRule =
        "A senha precisa ter pelo menos 8 caracteres, com letra maiúscula, letra minúscula e número.";

    [Required(ErrorMessage = "Informe seu nome.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "O nome deve ter entre {2} e {1} caracteres.")]
    [Display(Name = "Nome")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(256, ErrorMessage = "O e-mail deve ter no máximo {1} caracteres.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe uma senha.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = PasswordRule)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$",
        ErrorMessage = PasswordRule)]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirme a senha.")]
    [Compare(nameof(Password), ErrorMessage = "As duas senhas não são iguais.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmar senha")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
