using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace DevPath.UnitTests.Web.Lessons;

/// <summary>
/// Roda <c>wwwroot/js/skip-rule.js</c> no Node (#29): a tela só anuncia o "Seguir sem pontos" quando o
/// servidor também permite (LessonExerciseProgressDto.CanSkip), inclusive com max_attempts == failures_before_skip.
/// Sem Node no PATH o teste é pulado na máquina local; no CI (variável CI) ele falha para não sumir em silêncio.
/// </summary>
public sealed class SkipRuleScriptTests
{
    private static string ScriptPath([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "DevPath.Web")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "DevPath.Web", "wwwroot", "js", "skip-rule.js");
    }

    [NodeTheory]
    // until antes, remainingAttempts devolvido, until depois, anuncia?
    [InlineData("2", null, "1", false)]           // sem limite de tentativas, ainda falta 1
    [InlineData("1", null, "0", true)]            // sem limite: chegou ao limite, anuncia
    [InlineData("1", 3, "0", true)]               // ainda restam tentativas: o servidor permite pular
    [InlineData("1", 0, "0", false)]              // max_attempts == failures_before_skip: esgotou junto, não anuncia
    [InlineData("0", 0, "0", false)]              // já liberado ou esgotado: não anuncia de novo
    [InlineData("7", 6, "6", false)]              // N=7: nada de anúncio cedo
    [InlineData("", null, "", false)]             // sem progresso
    [InlineData("abc", null, "abc", false)]       // atributo inválido: fallback seguro
    [InlineData("-1", null, "-1", false)]
    public void AfterFailure_SoAnunciaQuandoOServidorPermitePular(string untilBefore, int? remaining, string untilAfter, bool announce)
    {
        var result = Run(untilBefore, remaining);

        result.GetProperty("until").GetString().Should().Be(untilAfter);
        result.GetProperty("announce").GetBoolean().Should().Be(announce);
    }

    [NodeFact]
    public void MaxAttemptsIgualAoLimite_NenhumaFalhaAnuncia()
    {
        // Simula N = MaxAttempts = 3: cada falha devolve remainingAttempts 2, 1, 0.
        var until = "3";
        foreach (var remaining in new[] { 2, 1, 0 })
        {
            var step = Run(until, remaining);
            step.GetProperty("announce").GetBoolean().Should().BeFalse($"remainingAttempts={remaining}");
            until = step.GetProperty("until").GetString()!;
        }

        until.Should().Be("0");
    }

    private static JsonElement Run(string untilBefore, int? remaining)
    {
        var script = $$"""
            const rule = require({{JsonSerializer.Serialize(ScriptPath())}});
            process.stdout.write(JSON.stringify(rule.afterFailure({{JsonSerializer.Serialize(untilBefore)}}, {{(remaining?.ToString() ?? "null")}})));
            """;
        var output = NodeRunner.Run(script);
        return JsonDocument.Parse(output).RootElement.Clone();
    }
}

internal static class NodeRunner
{
    public static bool Available { get; } = Probe();

    public static string? SkipReason => Available || Environment.GetEnvironmentVariable("CI") is not null
        ? null
        : "Node não encontrado no PATH (o CI exige).";

    public static string Run(string script)
    {
        var start = new ProcessStartInfo("node", ["-e", script])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o node.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000).Should().BeTrue("o node não deveria demorar");
        process.ExitCode.Should().Be(0, stderr);
        return stdout;
    }

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", ["--version"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null) return false;
            process.WaitForExit(10_000);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}

public sealed class NodeFactAttribute : FactAttribute
{
    public NodeFactAttribute() => Skip = NodeRunner.SkipReason;
}

public sealed class NodeTheoryAttribute : TheoryAttribute
{
    public NodeTheoryAttribute() => Skip = NodeRunner.SkipReason;
}
