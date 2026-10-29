using DevPath.Application.Journey;
using FluentAssertions;

namespace DevPath.UnitTests.Application;

public class EnrollmentResultTests
{
    [Fact]
    public void Factories_PreenchemTituloSoEmEnrolledEAlreadyEnrolled()
    {
        EnrollmentResult.Enrolled("SQL & Dados").Should().Be(new EnrollmentResult(EnrollmentResultKind.Enrolled, "SQL & Dados"));
        EnrollmentResult.AlreadyEnrolled("Atual").Should().Be(new EnrollmentResult(EnrollmentResultKind.AlreadyEnrolled, "Atual"));

        EnrollmentResult.PathNotFound().PathTitle.Should().BeNull();
        EnrollmentResult.NoLessons().PathTitle.Should().BeNull();
        EnrollmentResult.UserNotFound().PathTitle.Should().BeNull();
    }

    [Fact]
    public void HasPath_SoQuandoHaTrilha()
    {
        EnrollmentResult.Enrolled("A").HasPath.Should().BeTrue();
        EnrollmentResult.AlreadyEnrolled("A").HasPath.Should().BeTrue();
        EnrollmentResult.PathNotFound().HasPath.Should().BeFalse();
        EnrollmentResult.NoLessons().HasPath.Should().BeFalse();
        EnrollmentResult.UserNotFound().HasPath.Should().BeFalse();
    }

    [Fact]
    public void Kinds_ContratoComOFrontend()
    {
        Enum.GetNames<EnrollmentResultKind>().Should().Equal("Enrolled", "AlreadyEnrolled", "PathNotFound", "NoLessons", "UserNotFound");
    }
}
