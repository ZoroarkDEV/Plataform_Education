using System.Reflection;
using DevPath.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace DevPath.UnitTests.Infrastructure;

public sealed class PortugueseIdentityErrorDescriberTests
{
    private const string Email = "aluno@devpath.test";
    private static readonly PortugueseIdentityErrorDescriber Pt = new();
    private static readonly IdentityErrorDescriber En = new();

    private static readonly MethodInfo[] ErrorMethods = typeof(IdentityErrorDescriber)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => m.IsVirtual && m.ReturnType == typeof(IdentityError))
        .ToArray();

    public static TheoryData<string> AllMethods()
    {
        var data = new TheoryData<string>();
        foreach (var m in ErrorMethods) data.Add(m.Name);
        return data;
    }

    private static object?[] ArgsFor(MethodInfo m) =>
        m.GetParameters().Select(p => p.ParameterType == typeof(int) ? (object)8 : Email).ToArray();

    private static IdentityError Invoke(IdentityErrorDescriber d, string name)
    {
        var m = ErrorMethods.Single(x => x.Name == name);
        return (IdentityError)m.Invoke(d, ArgsFor(m))!;
    }

    [Fact]
    public void SobrescreveTodosOsMetodosDoIdentity()
    {
        ErrorMethods.Should().NotBeEmpty();
        foreach (var m in ErrorMethods)
        {
            var impl = typeof(PortugueseIdentityErrorDescriber).GetMethod(m.Name, m.GetParameters().Select(p => p.ParameterType).ToArray())!;
            impl.DeclaringType.Should().Be(typeof(PortugueseIdentityErrorDescriber), $"{m.Name} precisa ter texto em português");
        }
    }

    [Theory]
    [MemberData(nameof(AllMethods))]
    public void MantemOCodigoOriginal_ETrocaOTextoPorPortugues(string method)
    {
        var pt = Invoke(Pt, method);
        var en = Invoke(En, method);

        pt.Code.Should().Be(en.Code);
        pt.Description.Should().NotBeNullOrWhiteSpace().And.NotBe(en.Description);
        pt.Description.Should().EndWith(".");
        pt.Description.Should().NotContain(Email, "não ecoamos o e-mail digitado");
    }

    [Fact]
    public void EmailDuplicado_UsaAMesmaFraseSemRepetirOEmail()
    {
        Pt.DuplicateEmail(Email).Description.Should().Be("Já existe uma conta com este e-mail.");
        Pt.DuplicateUserName(Email).Description.Should().Be("Já existe uma conta com este e-mail.");
        Pt.DuplicateEmail(Email).Code.Should().Be(nameof(IdentityErrorDescriber.DuplicateEmail));
        Pt.DuplicateUserName(Email).Code.Should().Be(nameof(IdentityErrorDescriber.DuplicateUserName));
    }

    [Fact]
    public void EmailInvalido_NaoEcoaOEndereco()
    {
        Pt.InvalidEmail(Email).Description.Should().Be("E-mail inválido.");
        Pt.InvalidUserName(Email).Description.Should().Be("E-mail inválido.");
    }

    [Fact]
    public void RegrasDeSenha_UsamAFraseDaDica()
    {
        const string rule = "A senha precisa ter pelo menos 8 caracteres, com letra maiúscula, letra minúscula e número.";
        Pt.PasswordTooShort(8).Description.Should().Be(rule);
        Pt.PasswordTooShort(12).Description.Should().Contain("12 caracteres");
        Pt.PasswordRequiresDigit().Description.Should().Be(rule);
        Pt.PasswordRequiresLower().Description.Should().Be(rule);
        Pt.PasswordRequiresUpper().Description.Should().Be(rule);
        Pt.PasswordRequiresNonAlphanumeric().Description.Should().Be(rule).And.NotContain("símbolo");
        Pt.PasswordRequiresUniqueChars(3).Description.Should().Be(rule);
        PortugueseIdentityErrorDescriber.PasswordRule.Should().Be(rule);
    }
}
