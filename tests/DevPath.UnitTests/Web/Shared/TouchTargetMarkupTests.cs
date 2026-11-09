using FluentAssertions;

namespace DevPath.UnitTests.Web.Shared;

/// <summary>
/// Alvos de toque ≥ 44px fora da aula (#30, decisão sobre o não bloqueante do QA no #37):
/// menu mobile, login/cadastro, checkbox "Lembrar" e links de aula do dashboard.
/// </summary>
public sealed class TouchTargetMarkupTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "DevPath.Web")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "DevPath.Web");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root(), .. parts]));

    [Fact]
    public void Mobile_menu_links_and_account_actions_are_44px()
    {
        Read("Views", "Shared", "_Layout.cshtml")
            .Should().Contain("class=\"flex min-h-11 items-center rounded-md px-3 py-2 text-base font-medium");
        var partial = Read("Views", "Shared", "_LoginPartial.cshtml");
        partial.Should().Contain("? \"flex min-h-11 items-center rounded-md px-3 py-2 text-base font-medium");
        partial.Should().Contain("? \"flex min-h-11 items-center justify-center rounded-md border border-brand-600");
        partial.Should().Contain("? \"flex min-h-11 w-full items-center rounded-md");
    }

    [Fact]
    public void Form_controls_are_44px()
    {
        var css = Read("Styles", "app.css");
        css.Should().Contain("@apply mt-1 block min-h-11 w-full rounded-lg");
        css.Should().Contain("@apply inline-flex min-h-11 w-full items-center justify-center rounded-lg bg-brand-600");
        css.Should().Contain("@apply inline-flex min-h-11 items-center justify-center rounded-lg border border-slate-400");
        css.Should().Contain("@apply size-6 shrink-0 rounded");

        var login = Read("Views", "Account", "Login.cshtml");
        login.Should().Contain("<div class=\"flex min-h-11 items-center gap-3\">");
        login.Should().Contain("<label asp-for=\"RememberMe\" class=\"flex min-h-11 flex-1 cursor-pointer items-center");
        login.Should().Contain("class=\"link inline-flex min-h-11 items-center\">Criar conta</a>");
        Read("Views", "Account", "Register.cshtml")
            .Should().Contain("class=\"link inline-flex min-h-11 items-center\">Entrar</a>");
    }

    [Fact]
    public void Dashboard_lesson_links_are_44px()
    {
        Read("Views", "Dashboard", "Index.cshtml")
            .Should().Contain("<a href=\"@item.Lesson.Url\" class=\"link inline-flex min-h-11 items-center\">");
    }

    [Fact]
    public void Compiled_css_has_the_classes()
    {
        var css = Read("wwwroot", "css", "site.css");
        css.Should().Contain(".min-h-11{").And.Contain(".size-6{").And.Contain(".gap-3{");
        css.Should().MatchRegex(@"\.form-input\{[^}]*min-height");
        css.Should().MatchRegex(@"\.btn-primary\{[^}]*min-height");
        css.Should().MatchRegex(@"\.form-checkbox\{[^}]*width:calc\(var\(--spacing\) \* 6\)");
    }
}
