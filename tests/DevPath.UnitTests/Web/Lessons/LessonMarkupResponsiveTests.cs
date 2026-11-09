using System.Text.RegularExpressions;
using FluentAssertions;

namespace DevPath.UnitTests.Web.Lessons;

/// <summary>
/// 360px sem rolagem horizontal (#27): checagem de markup nas views da aula. Sem larguras fixas em px,
/// botões empilham (w-full sm:w-auto), textos longos quebram (min-w-0 / break-words), tabela do Executar rola no contêiner.
/// </summary>
public sealed class LessonMarkupResponsiveTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "DevPath.Web")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "DevPath.Web");
    }

    private static string View(string name) => File.ReadAllText(Path.Combine(Root(), "Views", "Lessons", name));

    public static TheoryData<string> Views => new()
    {
        "Show.cshtml", "_LessonCompletion.cshtml", "_LessonPager.cshtml", "_LessonExercise.cshtml",
        "_CompletedBadge.cshtml", "Unavailable.cshtml",
    };

    [Theory]
    [MemberData(nameof(Views))]
    public void No_fixed_widths(string view)
    {
        var html = View(view);
        Regex.IsMatch(html, @"\b(w|min-w)-\[\d+px\]").Should().BeFalse();
        Regex.IsMatch(html, @"style=""[^""]*width:\s*\d+px").Should().BeFalse();
        Regex.IsMatch(html, @"\bw-(9[6-9]|[1-9]\d{2,})\b").Should().BeFalse();
        html.Should().NotContain("whitespace-nowrap");
    }

    [Fact]
    public void Pager_items_shrink_and_wrap()
    {
        var pager = View("_LessonPager.cshtml");
        pager.Should().Contain("grid grid-cols-1 gap-3 sm:grid-cols-2", "uma coluna no celular (#37 H)");
        pager.Should().Contain("sm:col-start-2").And.Contain("sm:col-start-1");
        pager.Should().NotMatchRegex(@"[\s""]col-start-");
        pager.Should().Contain("min-w-0 break-words");
    }

    [Fact]
    public void Completion_message_wraps_and_buttons_stack()
    {
        var completion = View("_LessonCompletion.cshtml");
        completion.Should().Contain("break-words");
        completion.Should().Contain("w-full");
        completion.Should().Contain("sm:w-auto");
    }

    [Fact]
    public void Unavailable_buttons_are_full_width_on_phone()
    {
        var html = View("Unavailable.cshtml");
        Regex.Matches(html, @"<(a|span) [^>]*class=""inline-flex min-h-11 w-full items-center justify-center sm:w-auto rounded-lg")
            .Count.Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void Execute_results_table_scrolls_in_its_own_region()
    {
        var js = File.ReadAllText(Path.Combine(Root(), "wwwroot", "js", "site.js"));
        js.Should().Contain("<div class=\"table-scroll\" role=\"region\" tabindex=\"0\" aria-label=\"${regionLabel}\">");
        js.Should().Contain("Resultado: ");
        var css = File.ReadAllText(Path.Combine(Root(), "Styles", "app.css"));
        css.Should().Contain(".table-scroll {");
        css.Should().Contain("max-w-full overflow-x-auto");
    }

    [Fact]
    public void Live_region_is_stable_sibling_outside_completion_swap()
    {
        var show = View("Show.cshtml");
        show.Should().Contain("id=\"lesson-live\"");
        View("_LessonCompletion.cshtml").Should().NotContain("aria-live");
        View("_CompletedBadge.cshtml").Should().Contain("aria-hidden=\"true\">@LessonCompletionTexts.BadgeVisible");
    }

    [Fact]
    public void Hints_keep_focus_on_opened_summary_and_announce_next_politely()
    {
        var js = File.ReadAllText(Path.Combine(Root(), "wwwroot", "js", "site.js"));
        var start = js.IndexOf("const setupHints", StringComparison.Ordinal);
        start.Should().BePositive();
        var hints = js[start..js.IndexOf("const setup = ", start, StringComparison.Ordinal)];
        hints.Should().Contain("Dica ${index + 2} liberada logo abaixo.");
        hints.Should().NotContain(".focus(", "o foco fica no summary que o aluno abriu (UX #35)");
        js.Should().Contain("document.getElementById(\"lesson-live\") || status()");
    }

    [Fact]
    public void Hint_text_padding_is_in_compiled_css()
    {
        View("_LessonExercise.cshtml").Should().Contain("pb-2");
        var css = File.ReadAllText(Path.Combine(Root(), "wwwroot", "css", "site.css"));
        css.Should().Contain(".pb-2{");
    }

    [Fact]
    public void Touch_targets_are_at_least_44px()
    {
        var layout = File.ReadAllText(Path.Combine(Root(), "Views", "Shared", "_Layout.cshtml"));
        Regex.Matches(layout, @"<button type=""button"" data-(menu|theme)-toggle[^>]*class=""[^""]*min-h-11 min-w-11")
            .Count.Should().Be(2, "menu e tema do header com 44×44 (#37 H)");
        var css = File.ReadAllText(Path.Combine(Root(), "Styles", "app.css"));
        css.Should().Contain("@apply inline-flex min-h-11 items-center rounded-md border border-slate-500 px-3");
        View("Show.cshtml").Should().NotContain("data-theme-toggle", "o tema fica só no header (#37 I)");
        var compiled = File.ReadAllText(Path.Combine(Root(), "wwwroot", "css", "site.css"));
        compiled.Should().Contain(".min-w-11{").And.Contain(".grid-cols-1{").And.Contain(@".sm\:col-start-2{");
    }
}
