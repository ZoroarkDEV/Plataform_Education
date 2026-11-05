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
}
