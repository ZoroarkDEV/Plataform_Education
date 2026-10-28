using DevPath.Application.Content;
using DevPath.Domain.CareerPaths;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevPath.Infrastructure.Content;

/// <summary>
/// Importa as trilhas de <c>/content</c> para o banco fazendo upsert: cria o que não existe e
/// atualiza o que mudou. Rodar duas vezes seguidas não altera nada na segunda.
/// </summary>
/// <remarks>
/// <para>Cada item é encontrado no banco pelo slug, que nunca muda sozinho (o progresso do aluno fica preso a ele):</para>
/// <list type="bullet">
/// <item>Trilha: nome da pasta (<c>content/paths/sql-dados</c>).</item>
/// <item>Módulo: <c>slug</c> no <c>path.yaml</c> (igual ao nome da pasta do módulo), dentro da trilha.</item>
/// <item>Aula: nome do arquivo sem o prefixo de ordem (<c>02-joins.md</c> -> <c>joins</c>), dentro do módulo.</item>
/// </list>
/// <para>
/// Mudar título ou ordem atualiza a mesma linha. Como (trilha, ordem) e (módulo, ordem) são únicos no banco,
/// a reordenação é feita em dois passos na mesma transação: primeiro os itens que mudam de ordem são
/// "estacionados" numa ordem temporária alta (<see cref="ParkingOffset"/> + ordem atual), depois recebem a ordem final.
/// </para>
/// <para>
/// Nada é apagado. O que existe no banco e sumiu de /content: trilha é desativada, módulo e aula são despublicados,
/// e módulos e aulas vão para o fim da ordem, com aviso no log. Se voltarem para /content, são reativados/republicados.
/// </para>
/// </remarks>
public sealed class ContentSeeder(DevPathDbContext db, IContentSource source, ILogger<ContentSeeder> logger) : IContentSeeder
{
    /// <summary>Ordem temporária usada durante a reordenação (o CHECK do banco não aceita ordem negativa).</summary>
    public const int ParkingOffset = 1_000_000;

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

        var existing = await db.CareerPaths
            .Include(p => p.Modules)
            .ThenInclude(m => m.Lessons)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var plan = paths
            .OrderBy(p => p.Order)
            .Select(content => new PathPlan(content, existing.SingleOrDefault(p => p.Slug == content.Slug)))
            .ToList();

        var targets = plan.Where(p => p.Entity is not null)
            .Select(p => ComputeTargetOrders(p.Entity!, p.Content))
            .SelectMany(t => t)
            .ToDictionary(t => t.Key, t => t.Value, ReferenceEqualityComparer.Instance);

        var changes = new ChangeCounter();

        // Passo 1: estaciona quem vai mudar de ordem, para não bater no índice único no passo 2.
        foreach (var (entity, order) in targets)
        {
            Park(entity, order);
        }

        if (db.ChangeTracker.HasChanges())
        {
            changes.Collect(db);
            await db.SaveChangesAsync(cancellationToken);
        }

        // Passo 2: valores finais.
        foreach (var item in plan)
        {
            UpsertPath(item, targets);
        }

        var contentSlugs = paths.Select(p => p.Slug).ToHashSet();
        foreach (var orphan in existing.Where(p => !contentSlugs.Contains(p.Slug)))
        {
            logger.LogWarning(
                "Trilha '{Path}' existe no banco mas não em content/paths. Foi desativada; nada foi apagado.", orphan.Slug);
            if (orphan.IsActive)
            {
                orphan.Deactivate();
            }
        }

        changes.Collect(db);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = changes.ToResult();
        logger.LogInformation(
            "Seed de conteúdo concluído: {Paths} trilhas lidas, {Inserted} linhas inseridas, {Updated} atualizadas.",
            paths.Count, result.Inserted, result.Updated);

        return result;
    }

    /// <summary>
    /// Ordem final de cada módulo/aula que já existe no banco. Itens de /content ficam com a ordem do arquivo;
    /// órfãos vão para o fim, mantendo a ordem relativa entre eles.
    /// </summary>
    private static Dictionary<object, int> ComputeTargetOrders(CareerPath path, PathContent content)
    {
        var targets = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var moduleContents = content.Modules.ToDictionary(m => m.Slug);

        AssignTargets(path.Modules, m => m.Slug, m => m.Order,
            slug => moduleContents.TryGetValue(slug, out var c) ? c.Order : null, content.Modules.Count, targets);

        foreach (var module in path.Modules)
        {
            if (!moduleContents.TryGetValue(module.Slug, out var moduleContent))
            {
                continue; // módulo órfão: as aulas ficam como estão
            }

            var lessonOrders = moduleContent.Lessons.ToDictionary(l => l.Slug, l => l.Order);
            AssignTargets(module.Lessons, l => l.Slug, l => l.Order,
                slug => lessonOrders.TryGetValue(slug, out var order) ? order : null, moduleContent.Lessons.Count, targets);
        }

        return targets;
    }

    private static void AssignTargets<T>(
        IEnumerable<T> items, Func<T, string> slugOf, Func<T, int> orderOf,
        Func<string, int?> contentOrder, int contentCount, Dictionary<object, int> targets) where T : class
    {
        var next = contentCount;
        foreach (var item in items.OrderBy(orderOf))
        {
            targets[item] = contentOrder(slugOf(item)) ?? ++next;
        }
    }

    private static void Park(object entity, int target)
    {
        switch (entity)
        {
            case Module m when m.Order != target:
                m.UpdateDetails(m.Title, m.Description, ParkingOffset + m.Order, m.EstimatedMinutes);
                break;
            case Lesson l when l.Order != target:
                l.UpdateContent(l.Title, l.ContentType, l.Content, ParkingOffset + l.Order, l.EstimatedMinutes, l.CodeLanguage, l.Difficulty);
                break;
        }
    }

    private void UpsertPath(PathPlan item, Dictionary<object, int> targets)
    {
        var content = item.Content;
        var path = item.Entity;

        if (path is null)
        {
            path = CareerPath.Create(
                content.Title, content.Description, content.DifficultyLevel,
                content.EstimatedHours, content.Order, content.Icon);
            ApplySlug(path.Slug, content.Slug, path.ChangeSlug);
            db.CareerPaths.Add(path);
        }
        else
        {
            path.UpdateDetails(
                content.Title, content.Description, content.DifficultyLevel,
                content.EstimatedHours, content.Order, content.Icon);

            if (!path.IsActive)
            {
                logger.LogInformation("Trilha '{Path}' voltou para content/paths e foi reativada.", path.Slug);
                path.Activate();
            }
        }

        foreach (var moduleContent in content.Modules)
        {
            UpsertModule(path, moduleContent);
        }

        var moduleSlugs = content.Modules.Select(m => m.Slug).ToHashSet();
        foreach (var orphan in path.Modules.Where(m => !moduleSlugs.Contains(m.Slug)))
        {
            logger.LogWarning(
                "Módulo '{Module}' ('{Title}') da trilha '{Path}' existe no banco mas não em /content. "
                + "Foi despublicado e movido para o fim (ordem {Order}); nada foi apagado.",
                orphan.Slug, orphan.Title, path.Slug, targets[orphan]);

            orphan.UpdateDetails(orphan.Title, orphan.Description, targets[orphan], orphan.EstimatedMinutes);
            if (orphan.IsPublished)
            {
                orphan.Unpublish();
            }
        }
    }

    private void UpsertModule(CareerPath path, ModuleContent content)
    {
        // O tempo do módulo é a soma das aulas, para não ter dois números para manter.
        var estimatedMinutes = content.Lessons.Sum(l => l.EstimatedMinutes);

        var module = path.Modules.FirstOrDefault(m => m.Slug == content.Slug);
        if (module is null)
        {
            module = Module.Create(path.Id, content.Title, content.Order, content.Description, estimatedMinutes);
            ApplySlug(module.Slug, content.Slug, module.ChangeSlug);
            db.Modules.Add(module);
        }
        else
        {
            module.UpdateDetails(content.Title, content.Description, content.Order, estimatedMinutes);
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

        var lessonSlugs = content.Lessons.Select(l => l.Slug).ToHashSet();
        var nextOrder = content.Lessons.Count;
        foreach (var orphan in module.Lessons.Where(l => !lessonSlugs.Contains(l.Slug)).OrderBy(l => l.Order).ToList())
        {
            // A aula órfã some para o aluno (despublicada) e vai para o fim do módulo; o progresso continua preso a ela.
            var order = ++nextOrder;
            logger.LogWarning(
                "Aula '{Lesson}' ('{Title}') do módulo '{Module}' da trilha '{Path}' existe no banco mas não em /content. "
                + "Foi despublicada e movida para o fim do módulo (ordem {Order}); nada foi apagado.",
                orphan.Slug, orphan.Title, module.Slug, path.Slug, order);

            orphan.UpdateContent(orphan.Title, orphan.ContentType, orphan.Content, order,
                orphan.EstimatedMinutes, orphan.CodeLanguage, orphan.Difficulty);
            if (orphan.IsPublished)
            {
                orphan.Unpublish();
            }
        }
    }

    private void UpsertLesson(Module module, LessonContent content)
    {
        var lesson = module.Lessons.FirstOrDefault(l => l.Slug == content.Slug);
        if (lesson is null)
        {
            lesson = Lesson.Create(
                module.Id, content.Title, ContentType.Markdown, content.Body, content.Order,
                content.EstimatedMinutes, content.Language, content.Difficulty);
            ApplySlug(lesson.Slug, content.Slug, lesson.ChangeSlug);
            db.Lessons.Add(lesson);
            return;
        }

        lesson.UpdateContent(
            content.Title, ContentType.Markdown, content.Body, content.Order,
            content.EstimatedMinutes, content.Language, content.Difficulty);

        // Conteúdo que está versionado em /content é conteúdo publicado (a aula pode ter voltado depois de removida).
        if (!lesson.IsPublished)
        {
            logger.LogInformation("Aula '{Lesson}' do módulo '{Module}' voltou para /content e foi republicada.", lesson.Slug, module.Slug);
            lesson.Publish();
        }
    }

    // O Create do Domain gera o slug a partir do título; aqui o slug vem do arquivo/pasta.
    // SlugGenerator mantém hífens como separadores ("sql-dados" -> "sql-dados"), então o slug vai direto.
    private static void ApplySlug(string current, string wanted, Action<string> changeSlug)
    {
        if (current != wanted)
        {
            changeSlug(wanted);
        }
    }

    private sealed record PathPlan(PathContent Content, CareerPath? Entity);

    // Soma as mudanças dos dois SaveChanges sem contar a mesma linha duas vezes.
    private sealed class ChangeCounter
    {
        private readonly HashSet<object> _inserted = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> _updated = new(ReferenceEqualityComparer.Instance);

        public void Collect(DbContext db)
        {
            db.ChangeTracker.DetectChanges();
            foreach (var entry in db.ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Added)
                {
                    _inserted.Add(entry.Entity);
                }
                else if (entry.State == EntityState.Modified)
                {
                    _updated.Add(entry.Entity);
                }
            }
        }

        public ContentSeedResult ToResult() => new(_inserted.Count, _updated.Count(e => !_inserted.Contains(e)));
    }
}
