using DevPath.Application.Journey;
using DevPath.Application.Progress;
using DevPath.Domain.Progress;
using DevPath.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevPath.Infrastructure.Progress;

/// <summary>
/// As escritas de progresso de aula (ADR-007), compartilhadas pela Web (<see cref="LessonProgressService"/>) e pelo
/// Worker (conclusão automática no <c>pass</c>). Todas idempotentes:
/// <list type="bullet">
/// <item>Upsert (<c>INSERT ... ON CONFLICT</c>) no índice único parcial (aluno, aula): nunca duas linhas.</item>
/// <item>Concluir trava a matrícula ativa (<c>SELECT ... FOR UPDATE</c>) antes de decidir: dois cliques, duas abas
/// e o Worker do mesmo aluno entram em fila, e o recálculo do % da matrícula não perde atualização.</item>
/// </list>
/// </summary>
public sealed class LessonProgressWriter(DevPathDbContext db, JourneySnapshotLoader loader)
{
    /// <summary>Registra a abertura: cria InProgress ou só atualiza LastAccessedAt (concluída continua concluída).</summary>
    public Task MarkOpenedAsync(Guid userId, Guid lessonId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return db.Database.ExecuteSqlAsync($"""
            INSERT INTO "UserProgress" ("Id", "UserId", "LessonId", "Status", "TimeSpentSeconds", "LastAccessedAt")
            VALUES ({Guid.NewGuid()}, {userId}, {lessonId}, 'InProgress', 0, {now})
            ON CONFLICT ("UserId", "LessonId") WHERE "LessonId" IS NOT NULL
            DO UPDATE SET
                "LastAccessedAt" = EXCLUDED."LastAccessedAt",
                "Status" = CASE WHEN "UserProgress"."Status" = 'NotStarted' THEN 'InProgress' ELSE "UserProgress"."Status" END
            """, ct);
    }

    /// <summary>
    /// Trava a matrícula ativa do aluno até o fim da transação atual. false = sem matrícula ativa.
    /// Precisa de transação aberta.
    /// </summary>
    public async Task<bool> LockEnrollmentAsync(Guid userId, CancellationToken ct = default)
    {
        EnsureTransaction();
        // Chave composta (UserId, CareerPathId): sem coluna Id. Travamos a linha ativa pelo UserId.
        var locked = await db.Database
            .SqlQuery<Guid>($"""SELECT "UserId" AS "Value" FROM "UserPathEnrollments" WHERE "UserId" = {userId} AND "IsActive" FOR UPDATE""")
            .ToListAsync(ct);
        return locked.Count > 0;
    }

    /// <summary>
    /// Grava a conclusão (ou a promoção de "Seguir sem pontos" para ExercisePassed). Mantém o CompletedAt original
    /// numa promoção e não rebaixa Mastered. Precisa de transação aberta e da matrícula travada.
    /// </summary>
    public Task MarkCompletedAsync(Guid userId, Guid lessonId, LessonCompletionKind kind, int? points, CancellationToken ct = default)
    {
        EnsureTransaction();
        var now = DateTime.UtcNow;
        var kindText = kind.ToString();
        // #43: a primeira vez que o aluno usa o "Seguir sem pontos" fica gravada e sobrevive à promoção por pass.
        DateTime? skippedAt = kind == LessonCompletionKind.SkippedAfterFailures ? now : null;
        return db.Database.ExecuteSqlAsync($"""
            INSERT INTO "UserProgress" ("Id", "UserId", "LessonId", "Status", "Score", "TimeSpentSeconds", "CompletedAt", "LastAccessedAt", "CompletionKind", "SkippedAt")
            VALUES ({Guid.NewGuid()}, {userId}, {lessonId}, 'Completed', {points}::integer, 0, {now}, {now}, {kindText}, {skippedAt}::timestamptz)
            ON CONFLICT ("UserId", "LessonId") WHERE "LessonId" IS NOT NULL
            DO UPDATE SET
                "Status" = CASE WHEN "UserProgress"."Status" = 'Mastered' THEN 'Mastered' ELSE 'Completed' END,
                "Score" = EXCLUDED."Score",
                "CompletionKind" = EXCLUDED."CompletionKind",
                "CompletedAt" = CASE WHEN "UserProgress"."Status" IN ('Completed', 'Mastered')
                    THEN COALESCE("UserProgress"."CompletedAt", EXCLUDED."CompletedAt") ELSE EXCLUDED."CompletedAt" END,
                "LastAccessedAt" = EXCLUDED."LastAccessedAt",
                "SkippedAt" = COALESCE("UserProgress"."SkippedAt", EXCLUDED."SkippedAt")
            """, ct);
    }

    /// <summary>
    /// Recalcula as colunas da matrícula ativa (SPEC §4): ProgressPercentage = o % inteiro do dashboard e
    /// CurrentModuleId (ver <see cref="JourneySnapshot.CurrentModuleId"/>).
    /// </summary>
    public Task RecomputeEnrollmentAsync(Guid userId, JourneySnapshot snapshot, CancellationToken ct = default)
    {
        decimal percentage = snapshot.Dashboard.ProgressPercentage;
        var moduleId = snapshot.CurrentModuleId;
        return db.UserPathEnrollments
            .Where(e => e.UserId == userId && e.CareerPathId == snapshot.PathId && e.IsActive)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.ProgressPercentage, percentage)
                .SetProperty(e => e.CurrentModuleId, moduleId), ct);
    }

    /// <summary>
    /// Conclusão automática do Worker depois de gravar um <c>pass</c>, NA MESMA transação da submissão (Q1).
    /// Conclui (ou promove um "Seguir sem pontos"/AttemptsExhausted para ExercisePassed com os pontos cheios) quando a
    /// <see cref="LessonCompletionRule"/> dá ExercisePassed. Não faz nada se a aula não é da trilha atual do aluno,
    /// está bloqueada ou já foi concluída com ExercisePassed.
    /// </summary>
    /// <returns>true se concluiu ou promoveu agora (<c>ExecutionOutcome.LessonCompleted</c>).</returns>
    public async Task<bool> CompleteOnPassAsync(Guid userId, Guid exerciseId, CancellationToken ct = default)
    {
        EnsureTransaction();
        var lesson = await loader.FindLessonByExerciseAsync(exerciseId, ct);
        if (lesson is not { Visible: true })
        {
            return false;
        }

        var current = await loader.GetCurrentPathAsync(userId, ct);
        if (current.PathId != lesson.PathId || !await LockEnrollmentAsync(userId, ct))
        {
            return false;
        }

        var snapshot = await loader.LoadAsync(userId, lesson.PathId, current.PathSlug!, current.PathTitle!, ct);
        if (!snapshot.Contains(lesson.LessonId))
        {
            return false;
        }

        var status = snapshot.Lesson(lesson.LessonId).Status;
        if (status is JourneyLessonStatus.Locked or JourneyLessonStatus.ComingSoon)
        {
            return false;
        }

        if (snapshot.Progress.TryGetValue(lesson.LessonId, out var row)
            && row.Status is ProgressStatus.Completed or ProgressStatus.Mastered
            && row.CompletionKind == LessonCompletionKind.ExercisePassed)
        {
            return false; // já concluída com pontos: um pass a mais não muda nada
        }

        var decision = LessonCompletionRule.Evaluate(await loader.LoadAttemptsAsync(userId, lesson.LessonId, ct));
        if (decision.Kind != LessonCompletionKind.ExercisePassed)
        {
            return false; // outro exercício da aula ainda pendente
        }

        await MarkCompletedAsync(userId, lesson.LessonId, LessonCompletionKind.ExercisePassed, decision.Points, ct);
        await RecomputeEnrollmentAsync(userId,
            await loader.LoadAsync(userId, lesson.PathId, current.PathSlug!, current.PathTitle!, ct), ct);
        return true;
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Esta escrita de progresso precisa de uma transação aberta.");
        }
    }
}
