using System.ComponentModel.DataAnnotations;
using DevPath.Web.ViewModels.Account;
using FluentAssertions;

namespace DevPath.UnitTests.Web;

/// <summary>As Data Annotations dos ViewModels são o contrato de validação com o Frontend.</summary>
public class AccountViewModelsTests
{
    [Fact]
    public void Register_Valido_NaoTemErros()
    {
        Validate(ValidRegister()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("curta1A")]      // menos de 8
    [InlineData("semnumeroA")]   // sem dígito
    [InlineData("SEMMINUSCULA1")]
    [InlineData("semmaiuscula1")]
    public void Register_SenhaForaDaPolitica_FalhaNoCampoPassword(string password)
    {
        var model = ValidRegister();
        model.Password = model.ConfirmPassword = password;

        Validate(model).Should().Contain(r => r.MemberNames.Contains(nameof(RegisterViewModel.Password)));
    }

    [Fact]
    public void Register_ConfirmacaoDiferente_FalhaNoCampoConfirmPassword()
    {
        var model = ValidRegister();
        model.ConfirmPassword = "Outra1234";

        Validate(model).Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("As duas senhas não são iguais.");
    }

    [Fact]
    public void Login_SemEmailESenha_TemDoisErros()
    {
        Validate(new LoginViewModel()).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(typeof(RegisterViewModel))]
    [InlineData(typeof(LoginViewModel))]
    public void TodaRegraDeValidacao_TemMensagemEmPortugues(Type viewModel)
    {
        // [DataType(Password)] só descreve o campo (não valida), então fica de fora.
        var withoutMessage = viewModel.GetProperties()
            .SelectMany(p => p.GetCustomAttributes(typeof(ValidationAttribute), inherit: true)
                .Cast<ValidationAttribute>()
                .Where(a => a.GetType() != typeof(DataTypeAttribute) && string.IsNullOrWhiteSpace(a.ErrorMessage))
                .Select(a => $"{p.Name}: {a.GetType().Name}"))
            .ToList();

        withoutMessage.Should().BeEmpty("sem ErrorMessage o aluno veria a mensagem padrão em inglês");
    }

    [Fact]
    public void Register_EmailLongoDemais_MostraMensagemEmPortugues()
    {
        var model = ValidRegister();
        model.Email = new string('a', 250) + "@devpath.test";

        Validate(model).Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("O e-mail deve ter no máximo 256 caracteres.");
    }

    private static RegisterViewModel ValidRegister() => new()
    {
        DisplayName = "Ana",
        Email = "ana@devpath.test",
        Password = "Senha123",
        ConfirmPassword = "Senha123"
    };

    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
