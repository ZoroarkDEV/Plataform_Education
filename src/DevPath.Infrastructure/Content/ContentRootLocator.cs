namespace DevPath.Infrastructure.Content;

/// <summary>Descobre onde está a pasta <c>content/</c> do repositório.</summary>
public static class ContentRootLocator
{
    /// <summary>
    /// Usa <c>Content:RootPath</c> se estiver configurado (relativo a <paramref name="startDirectory"/>).
    /// Senão, sobe a partir de <paramref name="startDirectory"/> até achar uma pasta <c>content/paths</c>.
    /// Assim funciona tanto no <c>dotnet run</c> (src/DevPath.Web) quanto nos testes (bin/...).
    /// </summary>
    public static string Resolve(string? configuredPath, string startDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath, startDirectory);
        }

        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "content");
            if (Directory.Exists(Path.Combine(candidate, "paths")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"Pasta content/paths não encontrada a partir de '{startDirectory}'. " +
            "Configure Content:RootPath (variável de ambiente Content__RootPath).");
    }
}
