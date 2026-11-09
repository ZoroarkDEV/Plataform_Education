using DevPath.Application.Content;
using DevPath.Infrastructure.Content;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace DevPath.ContentTests;

/// <summary>
/// Testes de conteúdo (SPEC §9.4) rodando sobre a pasta /content de verdade do repositório.
/// Se alguém editar uma aula e quebrar o formato, estes testes falham no CI.
/// </summary>
public class RepositoryContentTests
{
    private static readonly string ContentRoot = ContentRootLocator.Resolve(null, AppContext.BaseDirectory);

    private static IReadOnlyList<PathContent> LoadPaths() => new FileSystemContentSource(ContentRoot).LoadPaths();

    [Fact]
    public void Conteudo_LeSemErrosDeArquivoOuFrontmatter()
    {
        var act = LoadPaths;

        act.Should().NotThrow();
    }

    [Fact]
    public void Conteudo_PassaNasRegrasDeConsistencia()
    {
        ContentValidator.Validate(LoadPaths()).Should().BeEmpty();
    }

    [Fact]
    public void Conteudo_TemAsDuasTrilhasIniciaisDaSpec()
    {
        // SPEC §14, item 4: Backend .NET e SQL & Dados.
        LoadPaths().Select(p => (p.Slug, p.Title)).Should().Equal(
            ("backend-net", "Backend .NET"),
            ("sql-dados", "SQL & Dados"));
    }

    [Fact]
    public void BackendNet_TemOsSeteModulosDaSpecEmOrdem()
    {
        // SPEC §7.1: Módulos 0 a 6 (aqui numerados de 1 a 7).
        var backend = LoadPaths().Single(p => p.Slug == "backend-net");

        backend.Modules.OrderBy(m => m.Order).Select(m => m.Title).Should().Equal(
            "Fundamentos de Programação",
            "C# Fundamentos",
            "SQL + Docker",
            "ASP.NET Core MVC",
            "Entity Framework Core",
            "APIs e Requisições",
            "Projeto Final");
    }

        [Fact]
    public void SqlDados_Modulos1e2_TemCompletionSubtitleDoRoteiro()
    {
        var sql = LoadPaths().Single(p => p.Slug == "sql-dados");
        sql.Modules.Single(m => m.Slug == "primeiros-passos-com-sql").CompletionSubtitle.Should().Be(
            "Você fez um banco de dados de verdade responder às suas perguntas, direto no navegador.");
        sql.Modules.Single(m => m.Slug == "mais-consultas-com-sql").CompletionSubtitle.Should().Be(
            "Agora você busca, filtra e cruza dados como quem trabalha com isso no dia a dia.");
    }

[Fact]
    public void SqlDados_PrimeirosPassosComSqlEhOModulo1()
    {
        // #22/#23: o primeiro contato com SQL (no navegador) vem antes do módulo de ambiente/Docker.
        var sql = LoadPaths().Single(p => p.Slug == "sql-dados");

        sql.Modules.OrderBy(m => m.Order).Select(m => (m.Order, m.Slug)).Should().Equal(
            (1, "primeiros-passos-com-sql"),
            (2, "mais-consultas-com-sql"),
            (3, "ambiente"),
            (4, "estruturas"),
            (5, "crud"),
            (6, "consultas"),
            (7, "performance-e-confiabilidade"),
            (8, "conectando-csharp"));
    }

    [Fact]
    public void TodoModulo_TemPeloMenosUmaAulaComTexto()
    {
        foreach (var module in LoadPaths().SelectMany(p => p.Modules))
        {
            module.Lessons.Should().NotBeEmpty($"o módulo '{module.Slug}' precisa de ao menos uma aula");
            module.Lessons.Should().OnlyContain(l => !string.IsNullOrWhiteSpace(l.Body) && l.EstimatedMinutes > 0);
        }
    }

    [Fact]
    public void ArquivosDeAula_TemPrefixoNumericoIgualAOrdem()
    {
        // 01-aula.md tem order: 1, assim a listagem de arquivos já mostra a ordem real.
        foreach (var lesson in LoadPaths().SelectMany(p => p.Modules).SelectMany(m => m.Lessons))
        {
            Path.GetFileName(lesson.SourceFile).Should().StartWith($"{lesson.Order:00}-", lesson.SourceFile);
        }
    }

    [Fact]
    public void SqlDados_PrimeirosPassos_TemAsCincoAulasDaIssue22NaOrdemCombinada()
    {
        // Ordem decidida pelo dono do projeto: JOIN antes de GROUP BY (issue #22).
        var module = LoadPaths().Single(p => p.Slug == "sql-dados").Modules.Single(m => m.Slug == "primeiros-passos-com-sql");

        module.Lessons.OrderBy(l => l.Order).Select(l => l.Slug).Should().Equal(
            "seu-primeiro-select",
            "filtrando-com-where",
            "ordenando-e-limitando-com-order-by-e-limit",
            "juntando-tabelas-com-join",
            "resumindo-dados-com-group-by");
    }

    [Fact]
    public void SqlDados_MaisConsultas_TemAsCincoAulasDaIssue41NaOrdemDoRoteiro()
    {
        // #41: o módulo 2 continua no navegador, cada aula com exercício.
        var module = LoadPaths().Single(p => p.Slug == "sql-dados").Modules.Single(m => m.Slug == "mais-consultas-com-sql");

        module.Title.Should().Be("Mais consultas com SQL");
        module.Lessons.OrderBy(l => l.Order).Select(l => l.Slug).Should().Equal(
            "buscando-parte-do-texto-com-like",
            "listas-e-faixas-com-in-e-between",
            "sem-repeticoes-com-distinct",
            "filtrando-grupos-com-having",
            "quem-ficou-sem-par-com-left-join");
        module.Lessons.Should().OnlyContain(l => !l.IsDraft && l.Exercise != null && l.Exercise.Dataset == "livraria");
    }

    [Fact]
    public void AulasComExercicio_TemFraseDeOQueVoceAprendeu()
    {
        // #41/#42: a tela de módulo concluído lista uma frase por aula. Toda aula pronta com exercício tem a sua.
        foreach (var lesson in AllLessons().Where(l => !l.IsDraft && l.Exercise != null))
        {
            lesson.CompletionSummary.Should().NotBeNullOrWhiteSpace($"{lesson.SourceFile} precisa de completion_summary");
            lesson.CompletionSummary!.Length.Should().BeLessThanOrEqualTo(200, lesson.SourceFile);
            lesson.CompletionSummary.Should().EndWith(".", lesson.SourceFile);
        }
    }

    [Fact]
    public void AulasEmConstrucao_EstaoMarcadasComoRascunho()
    {
        foreach (var lesson in AllLessons().Where(l => l.Body.Contains("Aula em construção")))
        {
            lesson.IsDraft.Should().BeTrue($"{lesson.SourceFile} é um stub e deve ter draft: true (aparece como 'Em breve')");
        }
    }

    [Fact]
    public void AulasProntas_TemObjetivosIguaisAoOQueVoceVaiAprender()
    {
        foreach (var lesson in AllLessons().Where(l => !l.IsDraft))
        {
            lesson.Objectives.Should().NotBeEmpty($"{lesson.SourceFile} é uma aula pronta e precisa de objectives");
            lesson.Body.Should().NotContain("Aula em construção", lesson.SourceFile);

            var section = Regex.Match(lesson.Body, @"## O que você vai aprender\n(?<items>(?:\n|- .*\n)+)");
            section.Success.Should().BeTrue($"{lesson.SourceFile} precisa da seção 'O que você vai aprender'");
            var bullets = Regex.Matches(section.Groups["items"].Value, @"^- (?<text>.+)$", RegexOptions.Multiline)
                .Select(m => Normalize(m.Groups["text"].Value));
            bullets.Should().Equal(lesson.Objectives.Select(Normalize), $"os objectives de {lesson.SourceFile} devem ser a mesma lista da seção");
        }

        // A seção usa `código`, **negrito** e ponto final; o frontmatter fica em texto puro.
        static string Normalize(string text) => text.Replace("`", "").Replace("**", "").Trim().TrimEnd('.');
    }

    [Fact]
    public void OQueVoceVaiAprender_EhSoALista_SeguidaDeOutroTitulo()
    {
        // A tela da aula esconde do corpo a seção "O que você vai aprender" até o próximo "## " e mostra
        // `objectives` no lugar. Qualquer texto entre a lista e o próximo título sumiria da tela.
        // Vale para as aulas prontas; os stubs (draft: true) aparecem como "Em breve".
        foreach (var lesson in AllLessons().Where(l => !l.IsDraft && l.Body.Contains("## O que você vai aprender")))
        {
            Regex.IsMatch(lesson.Body, @"## O que você vai aprender\n(?:\n|- .*\n)+## ").Should().BeTrue(
                $"em {lesson.SourceFile}, a seção 'O que você vai aprender' deve ter só a lista e ser seguida direto por outro título '## '");
        }
    }

    [Fact]
    public void Exercicios_TemSolucaoSoNoJsonECasosDeTeste()
    {
        var exercises = AllLessons().Where(l => l.Exercise is not null).ToList();

        exercises.Should().HaveCountGreaterThanOrEqualTo(5);
        foreach (var lesson in exercises)
        {
            var exercise = lesson.Exercise!;
            exercise.TestCases.Should().NotBeEmpty(exercise.SourceFile);
            exercise.Hints.Should().NotBeEmpty(exercise.SourceFile);
            exercise.CommonErrors.Should().NotBeEmpty(exercise.SourceFile);
            // Solução protegida (ADR-003): nunca aparece no texto da aula.
            lesson.Body.Should().NotContain(exercise.Solution.TrimEnd(';'), $"a solução de {exercise.SourceFile} não pode estar na aula");
        }
    }

    /// <summary>
    /// #41: a dica de erro comum compara texto, então os exemplos precisam ser o que o aluno escreve a partir do
    /// código inicial (com o apelido <c>AS total_livros</c> que já vem nele). #46: a tentativa mantém a linha de
    /// comentário do código inicial, como faz quem só completa o que já veio no editor.
    /// </summary>
    [Theory]
    [InlineData("WHERE COUNT(*) >= 3\nGROUP BY genero\n", "O WHERE filtra livro por livro")]
    [InlineData("GROUP BY genero\nHAVING COUNT(*) > 3;\n", "Veio só Romance")]
    [InlineData("HAVING COUNT(*) >= 3\nGROUP BY genero;\n", "A ordem das partes importa")]
    [InlineData("GROUP BY genero\nHAVING total_livros >= 3\n", "No HAVING, repita a conta")]
    [InlineData("GROUP BY genero\n", "Vieram os 4 gêneros")]
    public void Having_ErrosComunsCasamComOQueOAlunoEscreveAPartirDoCodigoInicial(string tail, string expectedStart)
    {
        var exercise = AllLessons().Single(l => l.Slug == "filtrando-grupos-com-having").Exercise!;
        exercise.StarterCode.Should().StartWith("--").And.Contain("AS total_livros").And.EndWith("GROUP BY genero\n");
        var attempt = exercise.StarterCode[..^"GROUP BY genero\n".Length] + tail;

        var message = DevPath.Application.CodeExecution.CommonErrorMatcher.Match(attempt,
            exercise.CommonErrors.Select(e => new DevPath.Domain.CareerPaths.CommonError(e.Example, e.Message)).ToList());

        message.Should().NotBeNull($"a tentativa '{attempt}' deveria receber a dica").And.StartWith(expectedStart);
    }

    /// <summary>
    /// #46: em todo exercício, cada erro comum continua recebendo a sua dica quando o aluno mantém as linhas de
    /// comentário (<c>--</c>) do código inicial acima da consulta.
    /// </summary>
    [Fact]
    public void ErrosComuns_CasamMesmoComOComentarioDoCodigoInicial()
    {
        var exercises = AllLessons().Where(l => l.Exercise is not null).Select(l => l.Exercise!).ToList();
        exercises.Should().NotBeEmpty();

        foreach (var exercise in exercises)
        {
            var commentLines = exercise.StarterCode.Split('\n').Where(line => line.TrimStart().StartsWith("--")).ToList();
            commentLines.Should().NotBeEmpty($"o código inicial de {exercise.SourceFile} deveria ter a linha de comentário");
            var commonErrors = exercise.CommonErrors
                .Select(e => new DevPath.Domain.CareerPaths.CommonError(e.Example, e.Message)).ToList();

            foreach (var error in exercise.CommonErrors)
            {
                var attempt = string.Join('\n', commentLines) + "\n" + error.Example;

                DevPath.Application.CodeExecution.CommonErrorMatcher.Match(attempt, commonErrors).Should().Be(error.Message,
                    $"em {exercise.SourceFile}, '{error.Example}' com o comentário do código inicial deveria receber a sua dica");
            }
        }
    }

    /// <summary>
    /// #41: o Resumo da aula não pode trazer a condição exata que o exercício pede (somada ao código inicial,
    /// ela vira a resposta pronta).
    /// </summary>
    [Fact]
    public void Resumo_NaoEntregaACondicaoDoExercicio()
    {
        foreach (var lesson in AllLessons().Where(l => l.Exercise is not null && l.Body.Contains("## Resumo")))
        {
            var solution = lesson.Exercise!.Solution.Trim().TrimEnd(';');
            var last = Regex.Matches(solution, @"\b(?:WHERE|HAVING)\s+(.+?)(?=\s+(?:GROUP BY|HAVING|ORDER BY|LIMIT)\b|$)",
                RegexOptions.IgnoreCase).LastOrDefault();
            if (last is null)
            {
                continue;
            }

            var condition = Regex.Replace(last.Groups[1].Value, @"\s+", " ").Trim();
            var resumo = Regex.Replace(lesson.Body[lesson.Body.IndexOf("## Resumo", StringComparison.Ordinal)..], @"\s+", " ");
            resumo.Should().NotContainEquivalentOf(condition, $"o Resumo de {lesson.SourceFile} não pode entregar a condição do exercício");
        }
    }

    [Fact]
    public void Exercicios_NenhumaDicaContemASolucao()
    {
        // Dicas guiam com pistas ou lacunas, sem entregar a query completa.
        foreach (var lesson in AllLessons().Where(l => l.Exercise is not null))
        {
            var exercise = lesson.Exercise!;
            var solution = NormalizeSql(exercise.Solution);
            solution.Should().NotBeNullOrWhiteSpace(exercise.SourceFile);

            foreach (var hint in exercise.Hints)
            {
                NormalizeSql(hint).Should().NotContain(solution,
                    $"a dica de {exercise.SourceFile} não pode conter a solução");
            }
        }
    }

    [Fact]
    public void TodasAsTrilhas_TemDescricaoCurtaESemJargao()
    {
        // #37 F: a descrição aparece no card de /escolha-sua-trilha para o aluno iniciante.
        string[] jargao = ["ASP.NET", "Entity Framework", "PostgreSQL", "Docker", "CRUD", "transaction", "modelagem"];

        foreach (var path in LoadPaths())
        {
            path.Description.Should().NotBeNullOrWhiteSpace($"a trilha '{path.Slug}' precisa de description");
            path.Description.Length.Should().BeLessThanOrEqualTo(ContentValidator.MaxDescriptionLength);
            foreach (var termo in jargao)
                path.Description.Should().NotContainEquivalentOf(termo, $"a description de '{path.Slug}' é lida por iniciantes (#37 F)");
        }
    }

    private static IEnumerable<LessonContent> AllLessons() =>
        LoadPaths().SelectMany(p => p.Modules).SelectMany(m => m.Lessons);

    private static string NormalizeSql(string sql)
    {
        var trimmed = sql.Trim().TrimEnd(';').Trim();
        return Regex.Replace(trimmed, @"\s+", " ").ToLowerInvariant();
    }
}
