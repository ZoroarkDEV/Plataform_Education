using DevPath.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;

namespace DevPath.UnitTests.Infrastructure;

public class RegisterErrorDescriptionsTests
{
    [Fact]
    public void Password_codes_use_the_password_rule()
    {
        var mismatch = new IdentityError { Code = nameof(IdentityErrorDescriber.PasswordMismatch), Description = "Senha incorreta." };
        RegisterErrorDescriptions.Describe([mismatch]).Should().Equal(PortugueseIdentityErrorDescriber.PasswordRule);
    }

    [Fact]
    public void Default_and_other_codes_use_create_account_fallback()
    {
        var fallback = new IdentityError
        {
            Code = nameof(IdentityErrorDescriber.DefaultError),
            Description = PortugueseIdentityErrorDescriber.GenericError
        };
        RegisterErrorDescriptions.Describe([fallback]).Should().Equal(RegisterErrorDescriptions.FailedToCreateAccount);
        RegisterErrorDescriptions.FailedToCreateAccount.Should().Be(
            "Não foi possível criar a conta. Confira os dados e tente de novo.");
        RegisterErrorDescriptions.FailedToCreateAccount.Should().NotBe(PortugueseIdentityErrorDescriber.GenericError);
    }

    [Fact]
    public void Duplicate_email_keeps_the_describer_text_once()
    {
        var email = new IdentityError
        {
            Code = nameof(IdentityErrorDescriber.DuplicateEmail),
            Description = PortugueseIdentityErrorDescriber.EmailAlreadyRegistered
        };
        var userName = new IdentityError
        {
            Code = nameof(IdentityErrorDescriber.DuplicateUserName),
            Description = PortugueseIdentityErrorDescriber.EmailAlreadyRegistered
        };
        RegisterErrorDescriptions.Describe([email, userName]).Should().Equal(
            PortugueseIdentityErrorDescriber.EmailAlreadyRegistered);
    }
}
