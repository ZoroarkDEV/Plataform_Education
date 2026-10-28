using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DevPath.Web.Lessons;

/// <summary>
/// HTML de uma aula, já separado nas partes que a tela posiciona (US-12.1):
/// o corpo principal e, se o Markdown tiver, as seções "O que você vai aprender" e "Resumo"
/// (títulos de nível 2). O exercício não vem do Markdown: vem de dados estruturados
/// (<c>NN-aula.exercise.json</c>), ver <c>ExerciseDto</c> em <c>LessonDto.Exercise</c>.
/// </summary>
public sealed record RenderedLesson(string ContentHtml, string? ObjectivesHtml, string? SummaryHtml);

/// <summary>
/// Converte o Markdown da aula em HTML seguro.
/// - HTML cru no Markdown é desligado (<c>DisableHtml</c>): sai como texto escapado.
/// - Links e imagens só com http(s), mailto ou caminho relativo; "javascript:" e afins viram "#".
/// - Blocos de código ganham destaque (SQL/C#, no servidor) e um botão "Copiar".
/// - O primeiro título de nível 1 é removido: a página já mostra o título da aula.
/// - Outro "# Título" no corpo não vira um segundo &lt;h1&gt;: os títulos do corpo descem um nível.
/// </summary>
public sealed class LessonMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseReferralLinks("noopener", "noreferrer")
        .DisableHtml()
        .Build();

    public RenderedLesson Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var document = Markdown.Parse(markdown, _pipeline);

        if (document.Count > 0 && document[0] is HeadingBlock { Level: 1 })
            document.RemoveAt(0);

        var objectives = ExtractSection(document, "o que voce vai aprender");
        var summary = ExtractSection(document, "resumo");

        // Depois de separar as seções: um "# Título" que sobrou no corpo viraria um segundo <h1>.
        DemoteHeadings(document, minLevel: 2);

        return new RenderedLesson(
            ToHtml(document),
            objectives is null ? null : ToHtml(objectives),
            summary is null ? null : ToHtml(summary));
    }

    /// <summary>Markdown curto (ex.: enunciado do exercício), com as mesmas regras de segurança.</summary>
    public string RenderFragment(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return ToHtml(Markdown.Parse(markdown, _pipeline));
    }

    /// <summary>Um bloco de código (com destaque e botão "Copiar"), sem passar pelo Markdown.</summary>
    public static string RenderCodeBlock(string code, string? language)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        LessonCodeBlockRenderer.WriteBlock(renderer, code, language);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>
    /// Tira do documento a seção "## {título}" (até o próximo título de nível 1 ou 2) e devolve
    /// o conteúdo dela sem o título. Comparação sem acento e sem maiúsculas.
    /// </summary>
    private static MarkdownDocument? ExtractSection(MarkdownDocument document, string normalizedTitle)
    {
        var start = -1;
        for (var i = 0; i < document.Count; i++)
        {
            if (document[i] is HeadingBlock { Level: 2 } heading && Normalize(InlineText(heading.Inline)) == normalizedTitle)
            {
                start = i;
                break;
            }
        }

        if (start < 0) return null;

        document.RemoveAt(start); // o título; a página escreve o dela
        var section = new MarkdownDocument();
        while (start < document.Count && document[start] is not HeadingBlock { Level: <= 2 })
        {
            var block = document[start];
            document.RemoveAt(start);
            section.Add(block);
        }

        return section;
    }

    /// <summary>
    /// Rebaixa os títulos do documento para que nenhum fique acima de <paramref name="minLevel"/>
    /// (a página já tem o &lt;h1&gt; com o título da aula). Mantém a hierarquia relativa: se sobrar
    /// um "# X" no corpo, todos descem um nível (h1→h2, h2→h3...), com teto em h6. Sem "# X",
    /// nada muda e os "## " das aulas continuam &lt;h2&gt;.
    /// </summary>
    private static void DemoteHeadings(MarkdownDocument document, int minLevel)
    {
        var headings = document.Descendants<HeadingBlock>().ToList();
        if (headings.Count == 0) return;

        var shift = minLevel - headings.Min(h => h.Level);
        if (shift <= 0) return;

        foreach (var heading in headings) heading.Level = Math.Min(heading.Level + shift, 6);
    }

    private string ToHtml(MarkdownDocument document)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer) { LinkRewriter = SafeUrl };
        _pipeline.Setup(renderer);

        var defaultCodeRenderer = renderer.ObjectRenderers.FindExact<CodeBlockRenderer>();
        if (defaultCodeRenderer is not null) renderer.ObjectRenderers.Remove(defaultCodeRenderer);
        renderer.ObjectRenderers.Insert(0, new LessonCodeBlockRenderer());

        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>Aceita http(s), mailto, âncoras e caminhos relativos. O resto (javascript:, data:...) vira "#".</summary>
    public static string SafeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "#";
        var trimmed = url.Trim();

        // Sem esquema = relativo ("/x", "x.png", "#secao"). Removemos espaços/controles que
        // navegadores ignoram, para "java\tscript:" não escapar da verificação.
        var compact = new string(trimmed.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
        var colon = compact.IndexOf(':');
        var firstSeparator = compact.IndexOfAny(['/', '?', '#']);
        var hasScheme = colon > 0 && (firstSeparator < 0 || colon < firstSeparator);
        if (!hasScheme) return trimmed;

        var scheme = compact[..colon].ToLowerInvariant();
        return scheme is "http" or "https" or "mailto" ? trimmed : "#";
    }

    private static string InlineText(ContainerInline? inline)
    {
        if (inline is null) return string.Empty;
        var text = new StringBuilder();
        foreach (var child in inline.Descendants<LiteralInline>()) text.Append(child.Content.ToString());
        foreach (var code in inline.Descendants<CodeInline>()) text.Append(code.Content);
        return text.ToString();
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd(':');
    }
}

/// <summary>
/// Bloco de código: barra com a linguagem e o botão "Copiar" (aparece só com JS, ver site.js),
/// e um &lt;pre&gt; com rolagem horizontal que recebe foco pelo teclado.
/// </summary>
internal sealed class LessonCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
{
    protected override void Write(HtmlRenderer renderer, CodeBlock block) =>
        WriteBlock(renderer, block.Lines.ToString(), block is FencedCodeBlock fenced ? fenced.Info : null);

    internal static void WriteBlock(HtmlRenderer renderer, string code, string? info)
    {
        var label = CodeHighlighter.LabelFor(info);
        var cssLanguage = CodeHighlighter.CssLanguage(info);
        var description = label is null ? "Exemplo de código" : $"Exemplo de código {label}";

        renderer.EnsureLine();
        renderer.Write("<figure class=\"code-block not-prose\" data-code-block>");
        renderer.Write("<figcaption class=\"code-block-bar\">");
        renderer.Write("<span class=\"code-block-lang\">").WriteEscape(label ?? "Código").Write("</span>");
        renderer.Write("<button type=\"button\" class=\"code-block-copy\" data-copy-code hidden aria-label=\"")
            .WriteEscape(label is null ? "Copiar código" : $"Copiar código {label}")
            .Write("\">Copiar</button>");
        renderer.Write("</figcaption>");
        renderer.Write("<pre tabindex=\"0\" aria-label=\"").WriteEscape(description).Write("\"><code");
        if (cssLanguage is not null) renderer.Write(" class=\"language-").WriteEscape(cssLanguage).Write("\"");
        renderer.Write(">");
        renderer.Write(CodeHighlighter.ToHtml(code, info));
        renderer.Write("</code></pre></figure>");
        renderer.WriteLine();
    }
}
