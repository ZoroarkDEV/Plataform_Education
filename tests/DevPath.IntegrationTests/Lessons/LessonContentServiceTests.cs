using DevPath.Application.Lessons;
using DevPath.Domain.CareerPaths;
using DevPath.Domain.Common;
using DevPath.Infrastructure.Lessons;
using DevPath.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DevPath.IntegrationTests.Lessons;

/// <summary>
/// Um Postgres para a classe inteira (IClassFixture): os dados são criados uma vez e os testes só leem.
/// </summary>
/// <remarks>
/// Módulo "Fundamentos SQL", na ordem:
/// 1 Select Básico · 2 Removida (despublicada) · 3 Joins · 4 Em Construção (rascunho) · 5 Vazia (sem conteúdo) · 6 Agregações.
/// Para a navegação contam só as publicadas: Select Básico(1), Joins(2), Em Construção(3), Vazia(4), Agregações(5).
/// </remarks>
public sealed class LessonContentFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var path = CareerPath.Create("Trilha de Banco de Dados");
        path.Activate();

        var module = path.AddModule("Fundamentos SQL");
        module.Publish();
        module.AddLesson("Select Básico", ContentType.Markdown, "# SELECT", estimatedMinutes: 10, codeLanguage: "sql",
            learningObjectives: ["Usar SELECT", "Filtrar com WHERE"]);
        module.AddLesson("Removida", ContentType.Markdown, "# Removida").Unpublish();
        module.AddLesson("Joins", ContentType.Markdown, "# JOIN", estimatedMinutes: 15, codeLanguage: "sql",
            difficulty: DifficultyLevel.Intermediate)
            .AddExercise("Autores e livros", "Liste **título** e **autor**.", "sql", "[{\"name\":\"caso\",\"rows\":[[\"LINHA_SECRETA\"]]}]",
                points: 10, starterCode: "SELECT ", solutionCode: "SELECT SOLUCAO_SECRETA;", dataset: "livraria",
                hints: ["Dica geral", "Dica mais direta"], commonErrors: [new("SELECT ERRO_COMUM_SECRETO;", "MENSAGEM_ERRO_SECRETA")]);
        module.AddLesson("Em Construção", ContentType.Markdown, "# Em breve", isDraft: true);
        module.AddLesson("Vazia", ContentType.Markdown, "   ");
        module.AddLesson("Agregações", ContentType.Markdown, "# GROUP BY")
            .AddExercise("Sem starter", "Conte os livros.", "sql", "[]", points: 15);

        // Módulo ainda não publicado: suas aulas não podem abrir.
        path.AddModule("Módulo Oculto").AddLesson("Aula Oculta", ContentType.Markdown, "# oculta");

        // Trilha desativada (o seed desativa trilhas removidas do /content).
        var inactive = CareerPath.Create("Trilha Desativada");
        inactive.Deactivate();
        var inactiveModule = inactive.AddModule("Modulo");
        inactiveModule.Publish();
        inactiveModule.AddLesson("Aula", ContentType.Markdown, "# aula");

        // Módulo à parte (para não mexer na navegação de "Fundamentos SQL"): jsonb de objetivos corrompido.
        var broken = path.AddModule("Modulo Quebrado");
        broken.Publish();
        broken.AddLesson("Objetivos Numericos", ContentType.Markdown, "# a");
        broken.AddLesson("Objetivos Com Nulo", ContentType.Markdown, "# b");
        broken.AddLesson("Rascunho Quebrado", ContentType.Markdown, "# c", isDraft: true);
        broken.AddLesson("Vizinha Boa", ContentType.Markdown, "# d", learningObjectives: ["Ok"]);

        // Exercício cujo .exercise.json saiu do /content: o seed despublica (#24).
        var orphanModule = path.AddModule("Modulo Exercicio Removido");
        orphanModule.Publish();
        orphanModule.AddLesson("Aula Com Exercicio Removido", ContentType.Markdown, "# e")
            .AddExercise("Removido", "Não aparece.", "sql", "[]").Unpublish();

        db.CareerPaths.AddRange(path, inactive);
        await db.SaveChangesAsync();

        // Grava JSON inválido direto no banco (o domínio nunca deixaria). São listas, então passam no CHECK.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Lessons\" SET \"LearningObjectives\" = '[1, 2]'::jsonb WHERE \"Slug\" = 'objetivos-numericos';");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Lessons\" SET \"LearningObjectives\" = '[\"ok\", null]'::jsonb WHERE \"Slug\" IN ('objetivos-com-nulo', 'rascunho-quebrado');");
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public DevPathDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DevPathDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}

public sealed class LessonContentServiceTests(LessonContentFixture fixture) : IClassFixture<LessonContentFixture>
{
    private const string PathSlug = "trilha-de-banco-de-dados";
    private const string ModuleSlug = "fundamentos-sql";

    private async Task<LessonLoadResult> LoadAsync(string lessonSlug, string pathSlug = PathSlug, string moduleSlug = ModuleSlug)
    {
        await using var db = fixture.CreateDbContext();
        return await new LessonContentService(db).GetLessonAsync(pathSlug, moduleSlug, lessonSlug);
    }

    [Fact]
    public async Task AulaDoMeio_TrazConteudoBreadcrumbVizinhosEPosicao()
    {
        var result = await LoadAsync("joins");

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
        var lesson = result.Lesson!;
        lesson.Title.Should().Be("Joins");
        lesson.MarkdownBody.Should().Be("# JOIN");
        lesson.EstimatedMinutes.Should().Be(15);
        lesson.Level.Should().Be(DifficultyLevel.Intermediate);
        lesson.Language.Should().Be("sql");
        lesson.Breadcrumb.Should().Be(new LessonBreadcrumb(PathSlug, "Trilha de Banco de Dados", ModuleSlug, "Fundamentos SQL"));

        // "Removida" (despublicada) fica fora: a anterior de Joins é Select Básico.
        lesson.PreviousLesson.Should().Be(new LessonLink("select-basico", "Select Básico", IsDraft: false));
        // O rascunho continua na navegação, marcado.
        lesson.NextLesson.Should().Be(new LessonLink("em-construcao", "Em Construção", IsDraft: true));
        lesson.Position.Should().Be(2);
        lesson.TotalInModule.Should().Be(5);
    }

    [Fact]
    public async Task PrimeiraAula_NaoTemAnterior_ETrazObjetivosDoBanco()
    {
        var lesson = (await LoadAsync("select-basico")).Lesson!;

        lesson.PreviousLesson.Should().BeNull();
        lesson.NextLesson!.Slug.Should().Be("joins");
        lesson.Position.Should().Be(1);
        // Ida e volta pelo jsonb.
        lesson.LearningObjectives.Should().Equal("Usar SELECT", "Filtrar com WHERE");
    }

    [Fact]
    public async Task UltimaAula_NaoTemProxima()
    {
        var lesson = (await LoadAsync("agregacoes")).Lesson!;

        lesson.NextLesson.Should().BeNull();
        lesson.PreviousLesson!.Slug.Should().Be("vazia");
        lesson.Position.Should().Be(5);
        lesson.TotalInModule.Should().Be(5);
        lesson.LearningObjectives.Should().BeEmpty();
    }

    [Theory]
    [InlineData("trilha-inexistente", ModuleSlug, "joins")]
    [InlineData(PathSlug, "modulo-inexistente", "joins")]
    [InlineData(PathSlug, ModuleSlug, "aula-inexistente")]
    [InlineData(PathSlug, "modulo-oculto", "aula-oculta")]          // módulo não publicado
    [InlineData("trilha-desativada", "modulo", "aula")]             // trilha inativa
    [InlineData(PathSlug, ModuleSlug, "removida")]                  // aula despublicada
    public async Task SlugErradoOuConteudoEscondido_RetornaNotFound(string pathSlug, string moduleSlug, string lessonSlug)
    {
        var result = await LoadAsync(lessonSlug, pathSlug, moduleSlug);

        result.IsSuccess.Should().BeFalse();
        result.Lesson.Should().BeNull();
        result.Error!.Kind.Should().Be(LessonLoadErrorKind.NotFound);
        result.Error.Detail.Should().StartWith("Aula não encontrada");
    }

    [Fact]
    public async Task AulaEmRascunho_AbertaDireto_RetornaDraft()
    {
        var result = await LoadAsync("em-construcao");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(new LessonLoadError(LessonLoadErrorKind.Draft, "Esta aula ainda está em construção."));
    }

    [Fact]
    public async Task AulaSemConteudo_RetornaMalformed()
    {
        var result = await LoadAsync("vazia");

        result.Error!.Kind.Should().Be(LessonLoadErrorKind.Malformed);
        result.Error.Detail.Should().Contain("Vazia").And.Contain("sem conteúdo");
    }

    [Fact]
    public async Task SlugsComMaiusculas_SaoNormalizados()
    {
        var result = await LoadAsync("Joins", "Trilha-De-Banco-De-Dados", "Fundamentos-SQL");
        result.IsSuccess.Should().BeTrue();
        result.Lesson!.Breadcrumb.PathSlug.Should().Be(PathSlug);
    }

    [Fact]
    public async Task ObjetivosAtualizados_SaoGravadosNoBanco()
    {
        // Garante que o ValueComparer detecta a troca da lista (sem ele o EF não gravaria nada).
        await using (var db = fixture.CreateDbContext())
        {
            var path = CareerPath.Create($"Trilha {Guid.NewGuid():N}");
            path.AddModule("M").AddLesson("Aula", ContentType.Markdown, "# a", learningObjectives: ["Antigo"]);
            db.CareerPaths.Add(path);
            await db.SaveChangesAsync();

            var lesson = await db.Lessons.SingleAsync(l => l.Id == path.Modules.Single().Lessons.Single().Id);
            lesson.UpdateContent(lesson.Title, lesson.ContentType, lesson.Content, lesson.Order, 0, null,
                DifficultyLevel.Beginner, ["Novo 1", "Novo 2"]);
            await db.SaveChangesAsync();

            await using var check = fixture.CreateDbContext();
            (await check.Lessons.AsNoTracking().SingleAsync(l => l.Id == lesson.Id))
                .LearningObjectives.Should().Equal("Novo 1", "Novo 2");
        }
    }

    [Theory]
    [InlineData("objetivos-numericos")]  // [1, 2]
    [InlineData("objetivos-com-nulo")]   // ["ok", null]
    public async Task ObjetivosInvalidosNoBanco_RetornaMalformedSemLancar(string lessonSlug)
    {
        var act = () => LoadAsync(lessonSlug, moduleSlug: "modulo-quebrado");

        var result = (await act.Should().NotThrowAsync()).Subject;
        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(LessonLoadErrorKind.Malformed);
        result.Error.Detail.Should().Contain("objetivos de aprendizagem inválidos").And.NotContain("Exception");
    }

    [Fact]
    public async Task ObjetivosInvalidosEmRascunho_RetornaDraft()
    {
        // A ordem das verificações continua: rascunho vem antes de "malformada".
        var result = await LoadAsync("rascunho-quebrado", moduleSlug: "modulo-quebrado");
        result.Error!.Kind.Should().Be(LessonLoadErrorKind.Draft);
    }

    [Fact]
    public async Task VizinhaDeAulaComObjetivosInvalidos_AbreNormalmente()
    {
        // A lista de vizinhos não lê o jsonb, então uma aula quebrada não derruba as outras.
        var lesson = (await LoadAsync("vizinha-boa", moduleSlug: "modulo-quebrado")).Lesson!;
        lesson.LearningObjectives.Should().Equal("Ok");
        lesson.PreviousLesson!.Slug.Should().Be("rascunho-quebrado");
        lesson.TotalInModule.Should().Be(4, "rascunhos contam no total do módulo");
    }

    [Theory]
    [InlineData("{\"a\": 1}")]
    [InlineData("\"texto\"")]
    [InlineData("1")]
    public async Task ObjetivosQueNaoSaoLista_SaoBarradosPeloCheckDoBanco(string json)
    {
        await using var db = fixture.CreateDbContext();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "UPDATE \"Lessons\" SET \"LearningObjectives\" = {0}::jsonb WHERE \"Slug\" = 'vizinha-boa';", json);

        (await act.Should().ThrowAsync<Npgsql.PostgresException>())
            .Which.ConstraintName.Should().Be("CK_Lessons_LearningObjectives_IsArray");
    }

    [Fact]
    public async Task AulaComExercicio_TrazOExercicioSemSolucaoNemCasosDeTeste()
    {
        var result = await LoadAsync("joins");

        result.Lesson!.Exercise.Should().Be(new ExerciseDto(
            Title: "Autores e livros",
            StatementMarkdown: "Liste **título** e **autor**.",
            Language: "sql",
            Dataset: "livraria",
            StarterCode: "SELECT ",
            Hints: result.Lesson.Exercise!.Hints,
            Points: 10));
        result.Lesson.Exercise.Hints.Should().Equal("Dica geral", "Dica mais direta");

        // Nada do que é só do servidor pode sair no DTO (nem serializado para a Web).
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().NotContain("SOLUCAO_SECRETA").And.NotContain("LINHA_SECRETA")
            .And.NotContain("ERRO_COMUM_SECRETO").And.NotContain("MENSAGEM_ERRO_SECRETA");
    }

    [Fact]
    public async Task ExercicioSemStarterCode_VemComStarterNuloEPontosDoBanco()
    {
        var exercise = (await LoadAsync("agregacoes")).Lesson!.Exercise!;
        exercise.StarterCode.Should().BeNull();
        exercise.Dataset.Should().BeNull();
        exercise.Hints.Should().BeEmpty();
        exercise.Points.Should().Be(15);
    }

    [Fact]
    public async Task ExercicioDespublicado_AulaAbreSemOExercicio()
    {
        var result = await LoadAsync("aula-com-exercicio-removido", moduleSlug: "modulo-exercicio-removido");
        result.Lesson.Should().NotBeNull();
        result.Lesson!.Exercise.Should().BeNull();
    }

    [Fact]
    public async Task AulaSemExercicio_ExerciseNulo()
    {
        (await LoadAsync("select-basico")).Lesson!.Exercise.Should().BeNull();
    }
}
