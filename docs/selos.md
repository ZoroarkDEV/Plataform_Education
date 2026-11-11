# Selos (#45, US-BADGE.1)

## "Primeiro SELECT"

| Campo | Valor |
|---|---|
| Id fixo | `5e1ec700-0001-4000-8000-000000000001` (`AchievementIds.PrimeiroSelect`) |
| Título | Primeiro SELECT |
| Descrição | Você fez um banco de dados responder à sua primeira pergunta. |
| Criteria (jsonb) | `{"slug":"primeiro-select"}` |
| Seed | migration `AddPrimeiroSelectAchievement` (idempotente, `ON CONFLICT DO NOTHING`; Down apaga os `UserAchievements` do selo e depois o selo) |

**Quando é concedido:** no primeiro acerto (`pass`) de **qualquer** exercício da vida do aluno, no Worker, na mesma
transação da `CodeSubmission` (`AchievementGranter.GrantOnPassAsync`, `INSERT ... ON CONFLICT DO NOTHING`).
Só quem de fato inseriu a linha recebe `ExecutionOutcome.NewlyUnlockedAchievement`; dois acertos simultâneos geram
um aviso só. Falha, erro, timeout, tentativas esgotadas e "Seguir sem pontos" nunca concedem. O selo não dá pontos.

**Backfill silencioso:** `IStudentAchievementService.ListAsync` concede primeiro o selo a quem já tinha direito
(uma tentativa `Pass` antiga, ou a aula `seu-primeiro-select` concluída com `ExercisePassed`), com `EarnedAt` = a data
do primeiro acerto, sem aviso de "desbloqueado". O mesmo vale se esse aluno acertar algo antes de abrir o dashboard.

**Ordem da lista:** do mais antigo para o mais novo (`EarnedAt` crescente, empate por título). Lista vazia = esconder
a área "Seus selos".

```csharp
public sealed record EarnedAchievementDto(string Title, string Description, DateTimeOffset EarnedAt);
Task<IReadOnlyList<EarnedAchievementDto>> IStudentAchievementService.ListAsync(Guid userId, CancellationToken ct = default);
// ExecutionOutcome(..., bool LessonCompleted = false, EarnedAchievementDto? NewlyUnlockedAchievement = null)
// JSON: "newlyUnlockedAchievement": { "title", "description", "earnedAt" } ou null
```
