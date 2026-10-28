using DevPath.Application.Content;
using DevPath.Domain.CareerPaths;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevPath.Infrastructure.Content;

/// <summary>
/// Importa as trilhas de <c>/content</c> para o banco fazendo upsert: cria o que não existe e
/// atualiza o que mudou. Rodar duas vezes seguidas não duplica nada (a segunda não altera linha alguma).
/// </summary>
/// <remarks>
/// <para>Como cada item é encontrado no banco:</para>
/// <list type="bullet">
/// <item>Trilha: pelo <c>Slug</c> (derivado do título, igual ao nome da pasta).</item>
/// <item>Módulo: pela <c>Order</c> dentro da trilha. Aula: pela <c>Order</c> dentro do módulo.</item>
/// </list>
/// <para>
/// As entidades do Domain só têm setters privados e não têm métodos de atualização. Para atualizar
/// sem mexer no Domain, o seeder altera os valores pelo <c>ChangeTracker</c> do EF Core (que enxerga
/// os setters privados). Criação continua passando pelas factories do Domain (<c>Create</c>).
/// </para>
/// <para>O seeder nunca apaga dados: o que existe no banco e sumiu de /content só gera um aviso no log.</para>
/// </remarks>
public sealed class ContentSeeder(DevPathDbContext db, IContentSource source, ILogger<ContentSeeder> logger) : IContentSeeder
{
    public async Task<ContentSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var paths = source.LoadPaths();
        ContentValidator.EnsureValid(paths);

        var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pending.Any())
        {
            throw new InvalidOperationException(
                "O banco tem migrations pendentes. Rode 'dotnet ef database update --project src/DevPath.Infrastructure' antes do seed.");
        }

        // Tudo ou nada: se algo falhar no meio, nenhuma trilha fica pela metade.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var path in paths.OrderBy(p => p.Order))
        {
            await UpsertPathAsync(path, cancellationToken);
        }

        var entries = db.ChangeTracker.Entries().ToList();
        var result = new ContentSeedResult(
            Inserted: entries.Count(e => e.State == EntityState.Added),
            Updated: entries.Count(e => e.State == EntityState.Modified));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Seed de conteúdo concluído: {Paths} trilhas lidas, {Inserted} linhas inseridas, {Updated} atualizadas.",
            paths.Count, result.Inserted, result.Updated);

        return result;
    }

    private async Task UpsertPathAsync(PathContent content, CancellationToken cancellationToken)
    {
        var slug = content.FolderName; // o ContentValidator garante que é o slug do título

        var path = await db.CareerPaths
            .Include(p => p.Modules)
            .ThenInclude(m => m.Lessons)
            .SingleOrDefaultAsync(p => p.Slug == slug, cancellationToken);

        if (path is null)
        {
            path = CareerPath.Create(
                content.Title, content.Description, content.DifficultyLevel,
                content.EstimatedHours, content.Order, content.Icon);
            db.CareerPaths.Add(path);
        }
        else
        {
            SetIfChanged(path, nameof(CareerPath.Title), content.Title);
            SetIfChanged(path, nameof(CareerPath.Description), content.Description);
            SetIfChanged(path, nameof(CareerPath.Icon), content.Icon);
            SetIfChanged(path, nameof(CareerPath.DifficultyLevel), content.DifficultyLevel);
            SetIfChanged(path, nameof(CareerPath.EstimatedHours), content.EstimatedHours);
            SetIfChanged(path, nameof(CareerPath.Order), content.Order);
        }

        foreach (var moduleContent in content.Modules)
        {
            UpsertModule(path, moduleContent);
        }

        WarnAboutOrphans(path, content);
    }

    private void UpsertModule(CareerPath path, ModuleContent content)
    {
        // O tempo do módulo é a soma das aulas, para não ter dois números para manter.
        var estimatedMinutes = content.Lessons.Sum(l => l.EstimatedMinutes);

        var module = path.Modules.FirstOrDefault(m => m.Order == content.Order);
        if (module is null)
        {
            module = Module.Create(path.Id, content.Title, content.Order, content.Description, estimatedMinutes);
            db.Modules.Add(module);
        }
        else
        {
            SetIfChanged(module, nameof(Module.Title), content.Title);
            SetIfChanged(module, nameof(Module.Description), content.Description);
            SetIfChanged(module, nameof(Module.EstimatedMinutes), estimatedMinutes);
        }

        // Conteúdo que está versionado em /content é conteúdo publicado.
        if (!module.IsPublished)
        {
            module.Publish();
        }

        foreach (var lessonContent in content.Lessons)
        {
            UpsertLesson(module, lessonContent);
        }
    }

    private void UpsertLesson(Module module, LessonContent content)
    {
        var lesson = module.Lessons.FirstOrDefault(l => l.Order == content.Order);
        if (lesson is null)
        {
            lesson = Lesson.Create(
                module.Id, content.Title, ContentType.Markdown, content.Body, content.Order,
                content.EstimatedMinutes, content.Language, content.Difficulty);
            db.Lessons.Add(lesson);
            return;
        }

        SetIfChanged(lesson, nameof(Lesson.Title), content.Title);
        SetIfChanged(lesson, nameof(Lesson.ContentType), ContentType.Markdown);
        SetIfChanged(lesson, nameof(Lesson.Content), content.Body);
        SetIfChanged(lesson, nameof(Lesson.EstimatedMinutes), content.EstimatedMinutes);
        SetIfChanged(lesson, nameof(Lesson.CodeLanguage), content.Language);
        SetIfChanged(lesson, nameof(Lesson.Difficulty), content.Difficulty);
    }

    // Só marca a propriedade como alterada se o valor mudou de verdade (é o que torna o seed idempotente).
    private void SetIfChanged<TEntity>(TEntity entity, string propertyName, object? value) where TEntity : class
    {
        var property = db.Entry(entity).Property(propertyName);
        if (!Equals(property.CurrentValue, value))
        {
            property.CurrentValue = value;
        }
    }

    private void WarnAboutOrphans(CareerPath path, PathContent content)
    {
        var moduleOrders = content.Modules.Select(m => m.Order).ToHashSet();
        foreach (var module in path.Modules.Where(m => !moduleOrders.Contains(m.Order)))
        {
            logger.LogWarning(
                "Módulo '{Module}' (ordem {Order}) da trilha '{Path}' existe no banco mas não em /content. Nada foi apagado.",
                module.Title, module.Order, path.Slug);
        }

        foreach (var moduleContent in content.Modules)
        {
            var module = path.Modules.FirstOrDefault(m => m.Order == moduleContent.Order);
            var lessonOrders = moduleContent.Lessons.Select(l => l.Order).ToHashSet();
            foreach (var lesson in module?.Lessons.Where(l => !lessonOrders.Contains(l.Order)) ?? [])
            {
                logger.LogWarning(
                    "Aula '{Lesson}' (ordem {Order}) do módulo '{Module}' existe no banco mas não em /content. Nada foi apagado.",
                    lesson.Title, lesson.Order, moduleContent.Slug);
            }
        }
    }
}
