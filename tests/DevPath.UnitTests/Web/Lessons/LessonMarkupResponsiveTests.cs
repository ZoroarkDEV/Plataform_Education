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
        pager.Should().Contain("grid-cols-2");
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
}
