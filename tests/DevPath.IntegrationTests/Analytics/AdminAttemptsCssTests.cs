using System.Text.RegularExpressions;

namespace DevPath.IntegrationTests.Analytics;

/// <summary>
/// O site.css é gerado pelo Tailwind e commitado (docs/frontend.md): se alguém muda a view e não regenera,
/// a classe não existe e a página quebra (ex.: sem overflow-x-auto a 360px a página rola para o lado). #43.
/// </summary>
public sealed partial class AdminAttemptsCssTests
{
    [Fact]
    public void TodaClasseDaPaginaAdmin_ExisteNoSiteCss()
    {
        var root = RepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "DevPath.Web", "Views", "Admin", "Tentativas.cshtml"));
        var css = File.ReadAllText(Path.Combine(root, "src", "DevPath.Web", "wwwroot", "css", "site.css"));

        // class="..." literais e as constantes de classe (const string X = "...") da view.
        var classes = ClassAttribute().Matches(view).Select(m => m.Groups[1].Value)
            .Concat(ClassConstant().Matches(view).Select(m => m.Groups[1].Value))
            .SelectMany(v => v.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct()
            .ToList();

        classes.Should().Contain(["overflow-x-auto", "px-3", "py-2", "text-right", "tabular-nums"],
            "a página depende delas para não vazar a 360px e para separar as colunas");
        classes.Where(c => !css.Contains("." + Escape(c), StringComparison.Ordinal))
            .Should().BeEmpty("rode o build do Tailwind (DevPath.Web) e commite o wwwroot/css/site.css");
    }

    // Seletor CSS como o Tailwind escreve: "dark:odd:bg-slate-800/50" vira ".dark\:odd\:bg-slate-800\/50".
    private static string Escape(string cls) => CssSpecial().Replace(cls, @"\$1");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DevPath.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("DevPath.sln não encontrado acima dos testes.");
    }

    [GeneratedRegex(@"class=""([^""@]*)""")]
    private static partial Regex ClassAttribute();

    [GeneratedRegex(@"const string \w+ = ""([^""]*)""")]
    private static partial Regex ClassConstant();

    [GeneratedRegex(@"([:/.\[\]])")]
    private static partial Regex CssSpecial();
}
