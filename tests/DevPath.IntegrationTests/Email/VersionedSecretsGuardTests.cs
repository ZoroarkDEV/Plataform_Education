using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevPath.IntegrationTests.Email;

/// <summary>
/// Guarda (#44): a chave do Resend só existe em variável de ambiente/secret. Nenhum arquivo versionado
/// pode ter valor em ApiKey (nem falso), e o .env.example deixa RESEND_API_KEY vazio.
/// </summary>
public sealed partial class VersionedSecretsGuardTests
{
    [GeneratedRegex(@"\bre_[A-Za-z0-9_]{16,}")]
    private static partial Regex ResendKeyPattern();

    [Fact]
    public void AppsettingsVersionados_NaoTemValorEmApiKey()
    {
        var files = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        files.Should().NotBeEmpty();

        var offenders = new List<string>();
        foreach (var file in files)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            Walk(json.RootElement, Path.GetFileName(file), offenders);
            ResendKeyPattern().IsMatch(File.ReadAllText(file)).Should().BeFalse($"{file} parece ter uma chave do Resend");
        }

        offenders.Should().BeEmpty("a chave do Resend vem só de Email__Resend__ApiKey/RESEND_API_KEY");
    }

    [Fact]
    public void EnvExample_ResendApiKeyVazia()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), ".env.example"));

        lines.Should().Contain("RESEND_API_KEY=");
        lines.Where(l => l.TrimStart().StartsWith("RESEND_API_KEY", StringComparison.Ordinal)
                         || l.Contains("Resend__ApiKey=", StringComparison.Ordinal))
            .Should().OnlyContain(l => l.Trim().EndsWith('='));
    }

    private static void Walk(JsonElement element, string path, List<string> offenders)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var childPath = $"{path}:{property.Name}";
                if (property.Name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
                    && !(property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == "")
                    && property.Value.ValueKind != JsonValueKind.Null)
                {
                    offenders.Add(childPath);
                }

                Walk(property.Value, childPath, offenders);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Walk(item, path, offenders);
            }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DevPath.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("DevPath.sln não encontrado.");
    }
}
