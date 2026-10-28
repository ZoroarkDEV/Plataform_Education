using DevPath.Application.CodeExecution;
using DevPath.Infrastructure.CodeExecution;
using FluentAssertions;

namespace DevPath.UnitTests.CodeExecution;

public class OutputComparerTests
{
    [Theory]
    [InlineData("n\nAna", "n\r\nAna\r\n")]
    [InlineData("n\nAna", "n  \nAna\t\n\n")]
    [InlineData("\nn\nAna\n", "n\nAna")]
    public void IgnoraFimDeLinhaEspacosELinhasVaziasNasPontas(string expected, string actual) =>
        OutputComparer.AreEquivalent(expected, actual).Should().BeTrue();

    [Theory]
    [InlineData("n\nAna", "n\nana")]
    [InlineData("n\nAna\nBia", "n\nBia\nAna")]
    [InlineData("n\nAna", "n\n Ana")]
    public void DiferencaReal_NaoEhIgnorada(string expected, string actual) =>
        OutputComparer.AreEquivalent(expected, actual).Should().BeFalse();
}

public class ExerciseTestCaseTests
{
    [Fact]
    public void LeLista_EDaNomeAosCasosSemNome()
    {
        var cases = ExerciseTestCase.ParseList("""[{"setup":"CREATE TABLE t(n int);","expectedOutput":"n"},{"name":"b","expectedOutput":"x"}]""");

        cases.Should().HaveCount(2);
        cases[0].Name.Should().Be("caso 1");
        cases[0].Setup.Should().Be("CREATE TABLE t(n int);");
        cases[1].Name.Should().Be("b");
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("""[{"name":"sem saída"}]""")]
    public void JsonInvalido_LancaFormatException(string json)
    {
        var act = () => ExerciseTestCase.ParseList(json);
        act.Should().Throw<FormatException>();
    }
}

public class DockerRunArgumentsTests
{
    private static readonly SandboxLimits Limits = new() { MemoryMb = 128, Cpus = 0.5, PidsLimit = 32, TmpfsSizeMb = 16 };

    private static IReadOnlyList<string> Build() =>
        DockerCliSandbox.BuildRunArguments("devpath-sandbox-abc", new SandboxRequest("postgres:16-alpine", ["sh", "-c", "echo oi"]), Limits);

    [Fact]
    public void TemTodasAsBarreirasDeIsolamento()
    {
        var args = string.Join(' ', Build());

        args.Should().StartWith("run --rm -i --name devpath-sandbox-abc");
        args.Should().Contain("--network none")
            .And.Contain("--memory 128m --memory-swap 128m")
            .And.Contain("--cpus 0.5")                       // ponto, mesmo em pt-BR
            .And.Contain("--pids-limit 32")
            .And.Contain("--read-only")
            .And.Contain("--tmpfs /tmp:rw,noexec,nosuid,nodev,size=16m")
            .And.Contain("--user 65534:65534")
            .And.Contain("--cap-drop ALL")
            .And.Contain("--security-opt no-new-privileges")
            .And.Contain("--pull never");
        args.Should().EndWith("postgres:16-alpine sh -c echo oi");
    }

    [Fact]
    public void NaoPublicaPortaNemMontaVolume()
    {
        Build().Should().NotContain(a => a == "-p" || a == "--publish" || a == "-P" || a == "-v" || a == "--volume"
                                         || a == "--privileged" || a.StartsWith("--network=host"));
    }
}
