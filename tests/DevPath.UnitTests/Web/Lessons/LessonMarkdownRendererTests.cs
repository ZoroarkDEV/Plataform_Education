using FluentAssertions;
using DevPath.Web.Lessons;

namespace DevPath.UnitTests.Web.Lessons;

public sealed class LessonMarkdownRendererTests
{
    private readonly LessonMarkdownRenderer _renderer = new();

    [Fact]
    public void Render_BlocoSql_DestacaNoServidorComBotaoCopiar()
    {
        var html = _renderer.Render("```sql\nSELECT nome FROM alunos WHERE id = 1; -- busca\n```").ContentHtml;

        html.Should().Contain("<figure class=\"code-block not-prose\" data-code-block>");
        html.Should().Contain("<code class=\"language-sql\">");
        html.Should().Contain("<span class=\"tok-keyword\">SELECT</span>");
        html.Should().Contain("<span class=\"tok-comment\">-- busca</span>");
        html.Should().Contain("data-copy-code hidden aria-label=\"Copiar código SQL\">Copiar</button>");
        html.Should().Contain("<pre tabindex=\"0\" aria-label=\"Exemplo de código SQL\">");
    }

    [Fact]
    public void Render_BlocoCSharp_DestacaPalavrasChaveEStrings()
    {
        var html = _renderer.Render("```csharp\nvar texto = \"oi\";\n```").ContentHtml;

        html.Should().Contain("<code class=\"language-csharp\">");
        html.Should().Contain("<span class=\"tok-keyword\">var</span>");
        html.Should().Contain("<span class=\"tok-string\">&quot;oi&quot;</span>");
        html.Should().Contain("aria-label=\"Copiar código C#\"");
    }

    [Fact]
    public void Render_LinguagemDesconhecida_SaiComoTextoEscapadoSemDestaque()
    {
        var html = _renderer.Render("```bash\necho \"<b>oi</b>\"\n```").ContentHtml;

        html.Should().Contain("<code class=\"language-bash\">echo &quot;&lt;b&gt;oi&lt;/b&gt;&quot;");
        html.Should().NotContain("tok-");
        html.Should().NotContain("<b>");
    }

    [Fact]
    public void Render_CodigoComHtmlDentro_NaoInjetaTags()
    {
        var html = _renderer.Render("```sql\nSELECT '<script>alert(1)</script>';\n```").ContentHtml;

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }

    [Theory]
    [InlineData("<script>alert('x')</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<div onclick=\"alert(1)\">clique</div>")]
    [InlineData("<iframe src=\"https://exemplo.com\"></iframe>")]
    public void Render_HtmlCru_EhEscapado(string markdown)
    {
        var html = _renderer.Render("Texto\n\n" + markdown + "\n\nInline " + markdown).ContentHtml;

        html.Should().NotContainAny("<script", "<img", "<div", "<iframe");
        html.Should().Contain("&lt;");
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JavaScript:alert(1))")]
    [InlineData("[x](data:text/html,oi)")]
    [InlineData("[x](vbscript:msgbox)")]
    [InlineData("![x](javascript:alert(1))")]
    public void Render_LinkPerigoso_ViraAncoraVazia(string markdown)
    {
        var html = _renderer.Render(markdown).ContentHtml;

        html.Should().NotContainEquivalentOf("javascript:");
        html.Should().NotContain("data:");
        html.Should().NotContain("vbscript:");
        html.Should().Contain("=\"#\"");
    }

    [Theory]
    [InlineData("https://learn.microsoft.com/dotnet")]
    [InlineData("/trilhas/sql-dados")]
    [InlineData("#resumo")]
    [InlineData("mailto:contato@exemplo.com")]
    public void SafeUrl_EnderecosSegurosPassam(string url) => LessonMarkdownRenderer.SafeUrl(url).Should().Be(url);

    [Fact]
    public void Render_LinkExterno_GanhaRelNoopener()
    {
        var html = _renderer.Render("[docs](https://learn.microsoft.com)").ContentHtml;

        html.Should().Contain("<a href=\"https://learn.microsoft.com\" rel=\"noopener noreferrer\">docs</a>");
    }

    [Fact]
    public void Render_SeparaSecoesERemoveTituloPrincipal()
    {
        const string markdown = """
            # Título da aula

            Introdução.

            ## O que você vai aprender

            - Item A

            ## Conceitos

            Corpo.

            ## Exercício

            Faça X.

            ## Resumo

            Fechamento.
            """;

        var result = _renderer.Render(markdown);

        result.ContentHtml.Should().NotContain("Título da aula");
        result.ContentHtml.Should().Contain("Introdução.").And.Contain("<h2>Conceitos</h2>").And.Contain("Corpo.");
        result.ContentHtml.Should().NotContainAny("Item A", "Fechamento.", "Resumo");
        result.ContentHtml.Should().Contain("<h2>Exercício</h2>").And.Contain("Faça X.",
            "o exercício vem do .exercise.json, não é mais extraído do Markdown");
        result.ObjectivesHtml.Should().Contain("<li>Item A</li>");
        result.SummaryHtml.Should().Contain("Fechamento.").And.NotContain("<h2>");
    }

    [Fact]
    public void Render_SemSecoesOpcionais_DevolveNull()
    {
        var result = _renderer.Render("Só texto.");

        result.ObjectivesHtml.Should().BeNull();
        result.SummaryHtml.Should().BeNull();
        result.ContentHtml.Should().Contain("<p>Só texto.</p>");
    }

    [Fact]
    public void Render_TituloDeSecaoSemAcentoOuMaiusculas_TambemSepara()
    {
        var result = _renderer.Render("Texto.\n\n## O QUE VOCE VAI APRENDER\n\n- A\n\n## resumo:\n\nFim.");

        result.ObjectivesHtml.Should().Contain("<li>A</li>");
        result.SummaryHtml.Should().Contain("Fim.");
    }

    [Fact]
    public void RenderFragment_EnunciadoComMarkdownEHtmlCru_FicaSeguro()
    {
        var html = _renderer.RenderFragment("Liste o **título**. <img src=x onerror=alert(1)> [x](javascript:alert(1))");

        html.Should().Contain("<strong>título</strong>");
        html.Should().NotContain("<img").And.NotContain("javascript:");
    }

    [Fact]
    public void RenderCodeBlock_CodigoInicial_ComDestaqueEBotaoCopiar()
    {
        var html = LessonMarkdownRenderer.RenderCodeBlock("SELECT <x>\nFROM livros;", "sql");

        html.Should().Contain("data-code-block").And.Contain("Copiar código SQL");
        html.Should().Contain("<span class=\"tok-keyword\">SELECT</span>");
        html.Should().Contain("&lt;x&gt;").And.NotContain("<x>");
    }

    [Fact]
    public void Render_TituloNivel1NoMeioDoCorpo_NaoGeraSegundoH1()
    {
        const string md = "# Seu primeiro SELECT\n\n## O que você vai aprender\n\n- Ler uma tabela\n\n" +
                          "Introdução.\n\n# Parte extra\n\n## Detalhe\n\n#### Nota\n\n###### Fundo\n\n" +
                          "## Resumo\n\n- SELECT lê dados";

        var result = _renderer.Render(md);

        result.ContentHtml.Should().NotContain("<h1");
        result.ContentHtml.Should().Contain("<h2>Parte extra</h2>")
            .And.Contain("<h3>Detalhe</h3>")
            .And.Contain("<h5>Nota</h5>")
            .And.Contain("<h6>Fundo</h6>")
            .And.NotContain("<h7");
        result.ObjectivesHtml.Should().Contain("Ler uma tabela");
        result.SummaryHtml.Should().Contain("SELECT lê dados");
        result.ContentHtml.Should().NotContain("Resumo").And.NotContain("O que você vai aprender");
    }

    [Fact]
    public void Render_SemTituloNivel1NoCorpo_MantemOsH2()
    {
        var html = _renderer.Render("# Aula\n\n## Tabelas\n\n### Colunas").ContentHtml;

        html.Should().Contain("<h2").And.Contain("<h3").And.NotContain("<h1").And.NotContain("<h4");
    }
}
