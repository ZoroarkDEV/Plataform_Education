using DevPath.Application.CodeExecution;
using DevPath.Domain.CareerPaths;
using FluentAssertions;

namespace DevPath.UnitTests.CodeExecution;

public class CommonErrorMatcherTests
{
    private static readonly CommonError TituloComAcento =
        new("SELECT título, preço FROM livros;", "Não existe coluna título. Os nomes das colunas são escritos sem acento: titulo, preco.");

    private static readonly CommonError AspasDuplas =
        new("SELECT titulo, preco FROM livros WHERE genero = \"Fantasia\" AND preco < 60;",
            "Textos em SQL vão entre aspas simples: 'Fantasia'. Aspas duplas são para nomes de colunas.");

    private static readonly CommonError SemVirgula =
        new("SELECT titulo preco FROM livros;",
            "Sua consulta trouxe 1 coluna, mas pedimos 2. Para pedir várias colunas, separe-as com vírgula: titulo, preco.");

    [Fact]
    public void Casa_IgnoraEspacosQuebrasDeLinhaPontoEVirgulaEMaiusculas()
    {
        var code = "  select   título,\n  preço   FROM   livros  ;;  ";

        CommonErrorMatcher.Match(code, [TituloComAcento, SemVirgula])
            .Should().Be(TituloComAcento.Message);
    }

    [Fact]
    public void Casa_DevolveOPrimeiroDaLista()
    {
        // Os dois examples normalizam para a mesma forma se o aluno mandar o primeiro.
        CommonErrorMatcher.Match("SELECT título, preço FROM livros;",
                [TituloComAcento, new(TituloComAcento.Example, "outra mensagem")])
            .Should().Be(TituloComAcento.Message);
    }

    [Fact]
    public void NaoCasa_QuandoSoOEspacoInternoDeIdentificadorDiffere() // sanity: Normalize não mexe em conteúdo
    {
        CommonErrorMatcher.Match("SELECT titulo,preco FROM livros;", [SemVirgula])
            .Should().BeNull("faltou o espaço entre titulo e preco: isso é outro SQL");
    }

    [Fact]
    public void Casa_ColapsoDeEspacoEntreTokens()
    {
        CommonErrorMatcher.Match("SELECT  titulo   preco  FROM   livros", [SemVirgula])
            .Should().Be(SemVirgula.Message);
    }

    [Fact]
    public void DistingueAspasSimplesDeDuplas()
    {
        CommonErrorMatcher.Match(
                "SELECT titulo, preco FROM livros WHERE genero = \"Fantasia\" AND preco < 60;",
                [AspasDuplas])
            .Should().Be(AspasDuplas.Message);

        CommonErrorMatcher.Match(
                "SELECT titulo, preco FROM livros WHERE genero = 'Fantasia' AND preco < 60;",
                [AspasDuplas])
            .Should().BeNull();
    }

    [Fact]
    public void SemMatch_OuListaVazia_DevolveNull()
    {
        CommonErrorMatcher.Match("SELECT titulo, preco FROM livros;", [TituloComAcento]).Should().BeNull();
        CommonErrorMatcher.Match("SELECT título, preço FROM livros;", []).Should().BeNull();
        CommonErrorMatcher.Match("   ;;;  ", [TituloComAcento]).Should().BeNull();
    }

    [Fact]
    public void NuncaDevolveOExample()
    {
        var message = CommonErrorMatcher.Match("SELECT título, preço FROM livros;", [TituloComAcento]);

        message.Should().Be(TituloComAcento.Message);
        message.Should().NotContain("SELECT").And.NotContain(TituloComAcento.Example);
    }

    [Theory]
    [InlineData("SELECT 1;", "SELECT 1")]
    [InlineData("  select\t1\n;\n;", "select 1")]
    [InlineData("SELECT título , preço FROM livros ;", "SELECT título , preço FROM livros")]
    public void Normalize_EspacosEPontoEVirgula(string input, string expected) =>
        CommonErrorMatcher.Normalize(input).Should().Be(expected);

    // #46: comentários SQL não contam para a dica.
    [Theory]
    [InlineData("-- Escreva sua consulta abaixo\nSELECT 1;", "SELECT 1")]
    [InlineData("SELECT 1; -- fim", "SELECT 1")]
    [InlineData("/* bloco */ SELECT 1", "SELECT 1")]
    [InlineData("SELECT /* várias\nlinhas */ titulo FROM livros", "SELECT titulo FROM livros")]
    [InlineData("SELECT/**/1", "SELECT 1")]
    [InlineData("SELECT 1 /* externo /* interno */ ainda comentário */ ;", "SELECT 1")]
    [InlineData("SELECT titulo -- comentário no meio\nFROM livros\n-- outra linha\nWHERE preco < 40;", "SELECT titulo FROM livros WHERE preco < 40")]
    [InlineData("SELECT 1 /* sem fechar", "SELECT 1")]
    [InlineData("-- só comentário", "")]
    public void Normalize_TiraComentarios(string input, string expected) =>
        CommonErrorMatcher.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("SELECT '--não é comentário' FROM livros", "SELECT '--não é comentário' FROM livros")]
    [InlineData("SELECT '/* nem isto */' FROM livros", "SELECT '/* nem isto */' FROM livros")]
    [InlineData("SELECT 'it''s -- dentro' -- fora\nFROM livros", "SELECT 'it''s -- dentro' FROM livros")]
    [InlineData("SELECT \"col--x\" FROM livros", "SELECT \"col--x\" FROM livros")]
    [InlineData("SELECT 'a  b' FROM livros", "SELECT 'a b' FROM livros")] // comportamento antigo: espaço colapsa até no literal
    public void Normalize_MantemComentarioDentroDeAspas(string input, string expected) =>
        CommonErrorMatcher.Normalize(input).Should().Be(expected);

    [Fact]
    public void Casa_QuandoOAlunoMantemALinhaDeComentarioDoCodigoInicial()
    {
        var code = "-- Escreva sua consulta abaixo\nSELECT título, preço\nFROM livros;";

        CommonErrorMatcher.Match(code, [TituloComAcento]).Should().Be(TituloComAcento.Message);
    }

    [Fact]
    public void Casa_ComComentarioNoMeioDaConsulta()
    {
        var code = "SELECT titulo, preco /* as duas colunas */\nFROM livros -- tabela\nWHERE genero = \"Fantasia\" AND preco < 60;";

        CommonErrorMatcher.Match(code, [AspasDuplas]).Should().Be(AspasDuplas.Message);
    }

    [Fact]
    public void NaoCasa_QuandoODashDashEstaDentroDoTexto()
    {
        // Se o '--' dentro do literal fosse tratado como comentário, sobraria "WHERE genero = '" e o resto sumiria.
        var withLiteral = new CommonError("SELECT titulo FROM livros WHERE genero = '--';", "mensagem");

        CommonErrorMatcher.Match("SELECT titulo FROM livros WHERE genero = '--';", [withLiteral]).Should().Be("mensagem");
        CommonErrorMatcher.Match("SELECT titulo FROM livros WHERE genero = '';", [withLiteral]).Should().BeNull();
    }

    [Fact]
    public void SoComentario_NaoCasa()
    {
        CommonErrorMatcher.Match("-- Escreva sua consulta abaixo\n", [TituloComAcento]).Should().BeNull();
    }
}
