using System.Text.Json;
using DevPath.Application.CodeExecution;
using DevPath.Infrastructure.CodeExecution;
using FluentAssertions;

namespace DevPath.UnitTests.CodeExecution;

/// <summary>Uma regra do PO por teste (issue #13). Os valores são escritos em JSON, como no banco.</summary>
public class ResultComparerTests
{
    // Monta o caso esperado a partir do JSON combinado, passando pelo mesmo ParseList da produção.
    private static TestCaseSpec Expected(string columns, string rows, bool orderMatters = false, bool checkNames = false) =>
        TestCaseSpec.ParseList($$"""[{"name":"c","columns":{{columns}},"rows":{{rows}},"orderMatters":{{(orderMatters ? "true" : "false")}},"checkColumnNames":{{(checkNames ? "true" : "false")}}}]""")[0];

    // Resultado do aluno: as linhas vêm como JSON (igual ao json_agg do runner).
    private static QueryResult Actual(string[] columns, string rows, bool truncated = false)
    {
        using var doc = JsonDocument.Parse(rows);
        var parsed = doc.RootElement.EnumerateArray()
            .Select(r => (IReadOnlyList<SqlValue>)r.EnumerateArray().Select(SqlValue.FromJson).ToList())
            .ToList();
        return new QueryResult(columns, parsed, truncated);
    }

    private static ResultComparison Compare(TestCaseSpec e, QueryResult a) => ResultComparer.Compare(e, a);

    [Fact]
    public void ResultadoIgual_Passa()
    {
        var r = Compare(Expected("""["titulo","preco"]""", """[["A",10],["B",null]]"""),
                        Actual(["titulo", "preco"], """[["A",10],["B",null]]"""));
        r.Passed.Should().BeTrue();
        r.Feedback.Should().BeNull();
    }

    [Fact]
    public void QuantidadeDeColunas_SempreConta()
    {
        var r = Compare(Expected("""["titulo","preco"]""", """[["A",10]]"""), Actual(["titulo"], """[["A"]]"""));
        r.Passed.Should().BeFalse();
        r.Feedback.Should().Contain("1 coluna").And.Contain("2 colunas");
    }

    [Fact]
    public void OrdemDasColunas_SempreConta()
    {
        var r = Compare(Expected("""["titulo","preco"]""", """[["A",10]]"""), Actual(["preco", "titulo"], """[[10,"A"]]"""));
        r.Passed.Should().BeFalse("trocar a ordem das colunas troca o tipo/valor de cada posição");
    }

    [Fact]
    public void NomesDasColunas_NaoContamPorPadrao()
    {
        var r = Compare(Expected("""["titulo"]""", """[["A"]]"""), Actual(["t"], """[["A"]]"""));
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void NomesDasColunas_ContamComCheckColumnNames()
    {
        var r = Compare(Expected("""["titulo","preco"]""", """[["A",10]]""", checkNames: true),
                        Actual(["titulo", "valor"], """[["A",10]]"""));
        r.Passed.Should().BeFalse();
        r.Feedback.Should().Contain("2ª coluna").And.Contain("\"preco\"").And.Contain("\"valor\"");
    }

    [Fact]
    public void NomesDasColunas_ComCheck_DiferenciamMaiusculas()
    {
        Compare(Expected("""["Titulo"]""", """[["A"]]""", checkNames: true), Actual(["titulo"], """[["A"]]"""))
            .Passed.Should().BeFalse();
    }

    [Fact]
    public void OrdemDasLinhas_NaoContaPorPadrao()
    {
        Compare(Expected("""["n"]""", """[["A"],["B"],["C"]]"""), Actual(["n"], """[["C"],["A"],["B"]]"""))
            .Passed.Should().BeTrue();
    }

    [Fact]
    public void OrdemDasLinhas_ContaComOrderMatters()
    {
        var r = Compare(Expected("""["n"]""", """[["A"],["B"]]""", orderMatters: true), Actual(["n"], """[["B"],["A"]]"""));
        r.Passed.Should().BeFalse();
        r.Feedback.Should().Contain("fora de ordem").And.Contain("ORDER BY");
    }

    [Fact]
    public void OrderMatters_NaOrdemCerta_Passa()
    {
        Compare(Expected("""["n"]""", """[["A"],["B"]]""", orderMatters: true), Actual(["n"], """[["A"],["B"]]"""))
            .Passed.Should().BeTrue();
    }

    [Fact]
    public void Multiconjunto_LinhaRepetidaPrecisaRepetirIgual()
    {
        // Mesmo conjunto {A, B}, mas o esperado tem A duas vezes: não pode passar.
        var r = Compare(Expected("""["n"]""", """[["A"],["A"],["B"]]"""), Actual(["n"], """[["A"],["B"],["B"]]"""));
        r.Passed.Should().BeFalse();
        r.Feedback.Should().Contain("(\"B\")");
    }

    [Fact]
    public void QuantidadeDeLinhas_Diferente_Falha()
    {
        var r = Compare(Expected("""["n"]""", """[["A"],["B"]]"""), Actual(["n"], """[["A"]]"""));
        r.Passed.Should().BeFalse();
        r.Feedback.Should().Contain("1 linha").And.Contain("2 linhas");
    }

    [Fact]
    public void ResultadoCortado_FalhaExplicandoOLimite()
    {
        var r = Compare(Expected("""["n"]""", """[["A"]]"""), Actual(["n"], """[["A"],["B"]]""", truncated: true));
        r.Passed.Should().BeFalse();
        r.Feedback.Should().Contain("mais de 2 linhas");
    }

    [Theory]
    [InlineData("29.9", "29.90")]
    [InlineData("10", "10.0")]
    [InlineData("10", "10.000")]
    [InlineData("0", "0.00")]
    [InlineData("-1.5", "-1.50")]
    [InlineData("1e2", "100")]
    public void Numeros_ComparamPeloValor(string expected, string actual) =>
        Compare(Expected("""["v"]""", $"[[{expected}]]"), Actual(["v"], $"[[{actual}]]")).Passed.Should().BeTrue();

    [Theory]
    [InlineData("29.9", "29.91")]
    [InlineData("10", "-10")]
    public void Numeros_DiferentesFalham(string expected, string actual) =>
        Compare(Expected("""["v"]""", $"[[{expected}]]"), Actual(["v"], $"[[{actual}]]")).Passed.Should().BeFalse();

    [Fact]
    public void NumeroForaDaFaixaDoDecimal_AindaComparaPeloValor() =>
        Compare(Expected("""["v"]""", "[[1e30]]"), Actual(["v"], "[[1.0e30]]")).Passed.Should().BeTrue();

    [Theory]
    [InlineData("null", "null", true)]
    [InlineData("null", "\"\"", false)]   // null não é texto vazio
    [InlineData("null", "0", false)]      // nem zero
    [InlineData("null", "false", false)]  // nem false
    [InlineData("\"\"", "null", false)]
    [InlineData("0", "null", false)]
    public void Null_SoBateComNull(string expected, string actual, bool passes) =>
        Compare(Expected("""["v"]""", $"[[{expected}]]"), Actual(["v"], $"[[{actual}]]")).Passed.Should().Be(passes);

    [Theory]
    [InlineData("true", "true", true)]
    [InlineData("false", "false", true)]
    [InlineData("true", "false", false)]
    [InlineData("true", "\"true\"", false)]  // bool não é texto
    [InlineData("true", "1", false)]         // nem número
    public void Bool_ComparaExato(string expected, string actual, bool passes) =>
        Compare(Expected("""["v"]""", $"[[{expected}]]"), Actual(["v"], $"[[{actual}]]")).Passed.Should().Be(passes);

    [Theory]
    [InlineData("\"Ana\"", "\"Ana\"", true)]
    [InlineData("\"Ana\"", "\"ana\"", false)]   // maiúsculas contam
    [InlineData("\"Ana\"", "\"Ana \"", false)]  // espaço conta
    [InlineData("\"29.90\"", "29.9", false)]    // texto que parece número não é número
    [InlineData("\"10\"", "10", false)]
    public void Texto_ComparaExato(string expected, string actual, bool passes) =>
        Compare(Expected("""["v"]""", $"[[{expected}]]"), Actual(["v"], $"[[{actual}]]")).Passed.Should().Be(passes);

    [Fact]
    public void ArrayDoAluno_NuncaBateComValorSimples() =>
        Compare(Expected("""["v"]""", """[["[1,2]"]]"""), Actual(["v"], "[[[1,2]]]")).Passed.Should().BeFalse();

    [Fact]
    public void ChaveDaLinha_NaoConfundeVirgulaDentroDoTexto() =>
        Compare(Expected("""["a","b"]""", """[["x,y","z"]]"""), Actual(["a", "b"], """[["x","y,z"]]""")).Passed.Should().BeFalse();

    [Fact]
    public void Feedback_NaoEntregaAsLinhasEsperadas()
    {
        var r = Compare(Expected("""["n"]""", """[["SEGREDO"]]"""), Actual(["n"], """[["outra"]]"""));
        r.Passed.Should().BeFalse();
        r.Feedback.Should().NotContain("SEGREDO").And.Contain("\"outra\"");
    }
}

public class TestCaseSpecTests
{
    [Fact]
    public void LeFormatoCombinado_ComPadroes()
    {
        var cases = TestCaseSpec.ParseList("""
            [
              { "name": "lista", "dataset": "livraria", "setup": "UPDATE livros SET preco = 1;",
                "columns": ["titulo", "preco"], "rows": [["A", 29.9], ["B", null], ["C", true]],
                "orderMatters": true, "checkColumnNames": true },
              { "columns": ["n"], "rows": [] }
            ]
            """);

        cases.Should().HaveCount(2);
        var first = cases[0];
        first.Name.Should().Be("lista");
        first.Dataset.Should().Be("livraria");
        first.Setup.Should().Be("UPDATE livros SET preco = 1;");
        first.Columns.Should().Equal("titulo", "preco");
        first.Rows[0][1].Should().Be(new SqlValue(SqlValueKind.Number, "29.9"));
        first.Rows[1][1].Should().Be(SqlValue.Null);
        first.Rows[2][1].Should().Be(SqlValue.Bool(true));
        first.OrderMatters.Should().BeTrue();
        first.CheckColumnNames.Should().BeTrue();

        var second = cases[1];
        second.Name.Should().Be("caso 2");
        second.Dataset.Should().BeNull();
        second.Setup.Should().BeNull();
        second.OrderMatters.Should().BeFalse("o padrão é comparar como multiconjunto");
        second.CheckColumnNames.Should().BeFalse("o padrão é não cobrar nomes");
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("[]")]
    [InlineData("""[{"name":"sem colunas","rows":[]}]""")]
    [InlineData("""[{"columns":["a","b"],"rows":[["só um"]]}]""")]
    [InlineData("""[{"columns":["a"],"rows":[[{"x":1}]]}]""")]
    [InlineData("""[{"columns":["a"],"rows":[],"expectedOutput":"formato antigo"}]""")]
    [InlineData("""[{"columns":["a"],"rows":[],"order_matters":true}]""")]
    public void FormatoInvalido_LancaFormatException(string json)
    {
        var act = () => TestCaseSpec.ParseList(json);
        act.Should().Throw<FormatException>();
    }
}

public class QueryResultParserTests
{
    [Fact]
    public void LeCabecalhoCsvEValoresJson()
    {
        // Saída real do runner (psql CSV + json_agg, que quebra linha entre os elementos).
        var stdout = "nome,nota\n[{\"nome\":\"Ana\",\"nota\":9.50}, \n {\"nome\":\"Caio\",\"nota\":null}]\n";

        var result = QueryResultParser.Parse(stdout, maxRows: 200);

        result.Columns.Should().Equal("nome", "nota");
        result.Rows.Should().HaveCount(2);
        result.Rows[0].Should().Equal(SqlValue.Text("Ana"), new SqlValue(SqlValueKind.Number, "9.50"));
        result.Rows[1][1].Should().Be(SqlValue.Null);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void NomesRepetidosEComCaracteresEspeciais_SaoPreservados()
    {
        var stdout = "a,a,\"lin\nha, \"\"x\"\"\"\n[{\"a\":1,\"a\":2,\"lin\\nha, \\\"x\\\"\":\"v\"}]\n";

        var result = QueryResultParser.Parse(stdout, 200);

        result.Columns.Should().Equal("a", "a", "lin\nha, \"x\"");
        result.Rows[0].Should().Equal(new SqlValue(SqlValueKind.Number, "1"), new SqlValue(SqlValueKind.Number, "2"), SqlValue.Text("v"));
    }

    [Fact]
    public void ResultadoVazio_TemColunasESemLinhas()
    {
        var result = QueryResultParser.Parse("nome\n[]\n", 200);
        result.Columns.Should().Equal("nome");
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void MaisLinhasQueOLimite_CortaEMarca()
    {
        var result = QueryResultParser.Parse("n\n[{\"n\":1},{\"n\":2},{\"n\":3}]\n", maxRows: 2);
        result.Rows.Should().HaveCount(2);
        result.Truncated.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("n\n")]
    [InlineData("n\nnão é json")]
    [InlineData("n\n{\"n\":1}")]
    [InlineData("a,b\n[{\"a\":1}]")]
    public void SaidaInesperada_LancaFormatException(string stdout)
    {
        var act = () => QueryResultParser.Parse(stdout, 200);
        act.Should().Throw<FormatException>();
    }
}

public class SqlErrorTranslatorTests
{
    [Theory]
    [InlineData("psql:/tmp/consulta.psql:4: ERROR:  42601: syntax error at or near \"\\\"", SqlErrorTranslator.MetaCommandMessage)]
    [InlineData("psql:/tmp/consulta.psql:4: ERROR:  42601: syntax error at or near \";\"", SqlErrorTranslator.SingleStatementMessage)]
    [InlineData("psql:/tmp/consulta.psql:4: ERROR:  42P11: cannot open multi-query plan as cursor", SqlErrorTranslator.SingleStatementMessage)]
    [InlineData("psql:/tmp/consulta.psql:4: ERROR:  42P11: cannot open COPY query as cursor", SqlErrorTranslator.ReadOnlyMessage)]
    [InlineData("psql:/tmp/consulta.psql:11: ERROR:  42501: permission denied to set role \"postgres\"", SqlErrorTranslator.PermissionMessage)]
    [InlineData("ERROR:  25006: cannot execute DELETE in a read-only transaction", SqlErrorTranslator.ReadOnlyMessage)]
    [InlineData("ERROR:  0A000: COPY is not allowed in a non-volatile function", SqlErrorTranslator.ReadOnlyMessage)]
    [InlineData("ERROR:  57014: canceling statement due to statement timeout", SqlErrorTranslator.TimeoutMessage)]
    [InlineData("algo que não é erro do postgres", SqlErrorTranslator.GenericMessage)]
    public void TraduzPeloSqlState(string stderr, string expected) =>
        SqlErrorTranslator.Translate(stderr).FriendlyMessage.Should().Be(expected);

    [Fact]
    public void TabelaInexistente_CitaONome()
    {
        var error = SqlErrorTranslator.Translate("ERROR:  42P01: relation \"livro\" does not exist");
        error.SqlState.Should().Be("42P01");
        error.FriendlyMessage.Should().Contain("\"livro\"");
        error.TechnicalDetail.Should().Contain("does not exist");
    }

    [Fact]
    public void Timeout_EhMarcado() =>
        SqlErrorTranslator.Translate("ERROR:  57014: canceling statement due to statement timeout").IsTimeout.Should().BeTrue();
}

public class PostgresSqlCodeRunnerUnitTests
{
    [Theory]
    [InlineData("SELECT 1;", "SELECT 1")]
    [InlineData("SELECT 1 ; ;\n\n", "SELECT 1")]
    [InlineData("SELECT ';'", "SELECT ';'")]
    public void NormalizeCode_TiraPontoEVirgulaDoFim(string code, string expected) =>
        PostgresSqlCodeRunner.NormalizeCode(code).Should().Be(expected);

    [Fact]
    public void NormalizeCode_RecusaCaractereNulo()
    {
        var act = () => PostgresSqlCodeRunner.NormalizeCode("SELECT 1\0; \\! id");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Preparacao_DatasetAntesDoSetup()
    {
        ExerciseSubmissionService.BuildPreparation("CREATE TABLE t(n int);", "INSERT INTO t VALUES (1);")
            .Should().Be("CREATE TABLE t(n int);\n;\nINSERT INTO t VALUES (1);\n");
        ExerciseSubmissionService.BuildPreparation(null, null).Should().BeNull();
    }

    [Fact]
    public async Task Stdin_TemTamanhoEmBytesNaFrente_EArgumentosSoNumeros()
    {
        var sandbox = new CapturingSandbox();
        await new PostgresSqlCodeRunner(sandbox, TimeSpan.FromSeconds(2)).RunAsync("SELECT 'zqxação';", "CREATE TABLE t();");

        var request = sandbox.Request!;
        request.Stdin.Should().Be("18\nSELECT 'zqxação'CREATE TABLE t();", "ç e ã ocupam 2 bytes cada em UTF-8");
        request.Command.Should().HaveCount(6);
        request.Command[4].Should().Be("2000");
        request.Command[5].Should().Be((PostgresSqlCodeRunner.MaxRows + 1).ToString());
        request.Command.Should().NotContain(a => a.Contains("zqx"), "o código do aluno só vai pelo stdin");
    }

    [Fact]
    public void Script_NaoUsaTrustNemLeOCodigoComoScript()
    {
        var script = PostgresSqlCodeRunner.Script;
        script.Should().NotContain("trust");
        script.Should().Contain("--auth=scram-sha-256");
        script.Should().NotContain("-f -", "o psql do aluno lê um script fixo, nunca o stdin");
        script.Should().NotMatchRegex(@"export\s+SU_PW|export\s+PGPASSWORD");
    }

    private sealed class CapturingSandbox : ICodeSandbox
    {
        public SandboxRequest? Request { get; private set; }

        public Task<SandboxResult> ExecuteAsync(SandboxRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new SandboxResult("", "", 0, TimeSpan.Zero, false, false, false, "fake"));
        }
    }
}

public class DatasetTests
{
    [Fact]
    public void Create_NormalizaSlugECalculaHash()
    {
        var dataset = DevPath.Domain.Datasets.Dataset.Create("Livraria", "CREATE TABLE t();");
        dataset.Slug.Should().Be("livraria");
        dataset.ContentHash.Should().HaveLength(64).And.Be(DevPath.Domain.Datasets.Dataset.ComputeHash("CREATE TABLE t();"));
    }

    [Fact]
    public void UpdateSql_SoMudaQuandoOTextoMuda()
    {
        var dataset = DevPath.Domain.Datasets.Dataset.Create("livraria", "A");
        dataset.UpdateSql("A").Should().BeFalse();
        var oldHash = dataset.ContentHash;
        dataset.UpdateSql("B").Should().BeTrue();
        dataset.ContentHash.Should().NotBe(oldHash);
        dataset.Sql.Should().Be("B");
    }
}
