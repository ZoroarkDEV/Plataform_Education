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
            "ordenando-com-order-by",
            "juntando-tabelas-com-join",
            "agregando-com-group-by");
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

    private static IEnumerable<LessonContent> AllLessons() =>
        LoadPaths().SelectMany(p => p.Modules).SelectMany(m => m.Lessons);
}
