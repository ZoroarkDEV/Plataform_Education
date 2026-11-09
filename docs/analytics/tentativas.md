# Histórico de tentativas do Executar (#43)

Para o dono descobrir onde os alunos travam. Duas formas de ver os mesmos números:

- **Página:** `GET /admin/tentativas` (só para o papel `Admin`; não há link no menu, abra a URL direto).
- **SQL:** as consultas abaixo, no `psql` do banco da aplicação. São as MESMAS que a página roda
  (`src/DevPath.Infrastructure/Analytics/AttemptAnalyticsSql.cs`; um teste falha se este arquivo e o código se separarem).

## De onde vêm os dados (reaproveitado, sem tabela nova)

Cada Executar que **rodou** já é gravado em `"CodeSubmissions"` (#25/#27): aluno (`UserId`), exercício
(`ExerciseId` → `"Exercises"."LessonId"` → aula), resultado e data/hora (`SubmittedAt`, UTC).

| Coluna | O que é |
|---|---|
| `Result` | `Pass` ou `Fail`. É o que as regras usam (limite de tentativas, "Seguir sem pontos"). Erro e timeout contam como `Fail`. |
| `Outcome` (**novo, #43**) | `Pass` (certo), `Fail` (errado), `Error` (erro do código, memória, saída grande) ou `Timeout`. `NULL` só em tentativas antigas que não passaram: antes do #43 erro e timeout eram gravados só como `Fail`. |

O "Seguir sem pontos" fica em `"UserProgress"` (uma linha por aluno e aula):

| Coluna | O que é |
|---|---|
| `CompletionKind` | Como a aula está concluída **agora**. `SkippedAfterFailures` = seguiu sem pontos. Um acerto depois troca para `ExercisePassed`. |
| `SkippedAt` (**novo, #43**) | Quando o aluno usou o "Seguir sem pontos" pela primeira vez. **Não some** quando ele acerta depois: é o histórico de quem usou. A migration preenche com `CompletedAt` quem está em `SkippedAfterFailures` hoje; quem já tinha sido promovido antes não é recuperável. |

**Não são gravados** (de propósito, não são tentativas): 400 (código vazio/grande), 429 (limite por minuto), 503
(executor fora), tentativas esgotadas e erro de preparação do exercício ("Erro de conteúdo" no log, culpa nossa).

**Aulas consideradas:** só as visíveis aos alunos: trilha ativa, módulo publicado, aula publicada e fora de rascunho
(`IsDraft`), e só exercícios publicados.

## 1. Quantas tentativas até acertar cada aula?

Por aluno e aula: tentativas nos exercícios da aula até o primeiro acerto, **inclusive** (acertou de primeira = 1).
Com mais de um exercício, a aula conta como acertada quando todos tiveram um `Pass`, e valem as tentativas até o
último desses primeiros acertos. Tentativas **depois** do acerto não contam. Quem ainda não acertou entra só em
`StudentsTried` (fica fora da mediana, da média e da distribuição).

- `MedianAttempts`: mediana (`percentile_cont(0.5)`). `AverageAttempts`: média.
- Distribuição: quantos alunos acertaram com 1, 2, 3, 4, 5, 6 a 10 e 11 ou mais tentativas.
- "Aula 1": filtre pela linha de `ModuleOrder = 1` e `LessonOrder = 1` (ou pelo título).

```sql
WITH visible_lessons AS (
    SELECT l."Id" AS lesson_id, p."Order" AS path_order, p."Title" AS path_title,
           m."Order" AS module_order, m."Title" AS module_title, l."Order" AS lesson_order, l."Title" AS lesson_title
    FROM "Lessons" l
    JOIN "Modules" m ON m."Id" = l."ModuleId"
    JOIN "CareerPaths" p ON p."Id" = m."CareerPathId"
    WHERE p."IsActive" AND m."IsPublished" AND l."IsPublished" AND NOT l."IsDraft"
),
exercises AS (
    SELECT e."Id" AS exercise_id, e."LessonId" AS lesson_id
    FROM "Exercises" e
    JOIN visible_lessons vl ON vl.lesson_id = e."LessonId"
    WHERE e."IsPublished"
),
first_pass AS (
    SELECT s."UserId" AS user_id, x.lesson_id, x.exercise_id, MIN(s."SubmittedAt") AS passed_at
    FROM "CodeSubmissions" s
    JOIN exercises x ON x.exercise_id = s."ExerciseId"
    WHERE s."Result" = 'Pass'
    GROUP BY s."UserId", x.lesson_id, x.exercise_id
),
lesson_pass AS (
    SELECT fp.user_id, fp.lesson_id, MAX(fp.passed_at) AS passed_at
    FROM first_pass fp
    GROUP BY fp.user_id, fp.lesson_id
    HAVING COUNT(*) = (SELECT COUNT(*) FROM exercises x WHERE x.lesson_id = fp.lesson_id)
),
attempts AS (
    SELECT lp.lesson_id, lp.user_id, COUNT(*) AS attempts
    FROM lesson_pass lp
    JOIN exercises x ON x.lesson_id = lp.lesson_id
    JOIN "CodeSubmissions" s ON s."ExerciseId" = x.exercise_id AND s."UserId" = lp.user_id
                            AND s."SubmittedAt" <= lp.passed_at
    GROUP BY lp.lesson_id, lp.user_id
),
tried AS (
    SELECT x.lesson_id, COUNT(DISTINCT s."UserId") AS students
    FROM exercises x
    JOIN "CodeSubmissions" s ON s."ExerciseId" = x.exercise_id
    GROUP BY x.lesson_id
)
SELECT vl.lesson_id AS "LessonId", vl.path_title AS "PathTitle", vl.module_order AS "ModuleOrder",
       vl.module_title AS "ModuleTitle", vl.lesson_order AS "LessonOrder", vl.lesson_title AS "LessonTitle",
       COALESCE(MAX(t.students), 0)::int AS "StudentsTried",
       COUNT(a.user_id)::int AS "StudentsPassed",
       percentile_cont(0.5) WITHIN GROUP (ORDER BY a.attempts) AS "MedianAttempts",
       AVG(a.attempts)::float8 AS "AverageAttempts",
       COUNT(*) FILTER (WHERE a.attempts = 1)::int AS "One",
       COUNT(*) FILTER (WHERE a.attempts = 2)::int AS "Two",
       COUNT(*) FILTER (WHERE a.attempts = 3)::int AS "Three",
       COUNT(*) FILTER (WHERE a.attempts = 4)::int AS "Four",
       COUNT(*) FILTER (WHERE a.attempts = 5)::int AS "Five",
       COUNT(*) FILTER (WHERE a.attempts BETWEEN 6 AND 10)::int AS "SixToTen",
       COUNT(*) FILTER (WHERE a.attempts >= 11)::int AS "ElevenOrMore"
FROM visible_lessons vl
LEFT JOIN tried t ON t.lesson_id = vl.lesson_id
LEFT JOIN attempts a ON a.lesson_id = vl.lesson_id
WHERE EXISTS (SELECT 1 FROM exercises x WHERE x.lesson_id = vl.lesson_id)
GROUP BY vl.lesson_id, vl.path_order, vl.path_title, vl.module_order, vl.module_title, vl.lesson_order, vl.lesson_title
ORDER BY vl.path_order, vl.path_title, vl.module_order, vl.lesson_order;
```

## 2. Em qual aula mais gente usa "Seguir sem pontos"?

Uma linha por aula com pelo menos um uso, a que mais usou primeiro. `StudentsSkipped` = alunos que usaram alguma vez;
`StillSkipped` = continuam sem pontos; `PassedAfterSkip` = acertaram depois.

```sql
WITH visible_lessons AS (
    SELECT l."Id" AS lesson_id, p."Order" AS path_order, p."Title" AS path_title,
           m."Order" AS module_order, m."Title" AS module_title, l."Order" AS lesson_order, l."Title" AS lesson_title
    FROM "Lessons" l
    JOIN "Modules" m ON m."Id" = l."ModuleId"
    JOIN "CareerPaths" p ON p."Id" = m."CareerPathId"
    WHERE p."IsActive" AND m."IsPublished" AND l."IsPublished" AND NOT l."IsDraft"
)
SELECT vl.lesson_id AS "LessonId", vl.path_title AS "PathTitle", vl.module_order AS "ModuleOrder",
       vl.module_title AS "ModuleTitle", vl.lesson_order AS "LessonOrder", vl.lesson_title AS "LessonTitle",
       COUNT(*)::int AS "StudentsSkipped",
       COUNT(*) FILTER (WHERE up."CompletionKind" = 'SkippedAfterFailures')::int AS "StillSkipped",
       COUNT(*) FILTER (WHERE up."CompletionKind" = 'ExercisePassed')::int AS "PassedAfterSkip"
FROM visible_lessons vl
JOIN "UserProgress" up ON up."LessonId" = vl.lesson_id
WHERE up."SkippedAt" IS NOT NULL
GROUP BY vl.lesson_id, vl.path_order, vl.path_title, vl.module_order, vl.module_title, vl.lesson_order, vl.lesson_title
ORDER BY "StudentsSkipped" DESC, vl.path_order, vl.path_title, vl.module_order, vl.lesson_order;
```

## 3. Taxa de falha por aula

`FailureRatePercent` = tentativas que não passaram ÷ tentativas × 100 (uma casa). `Wrong` = errado; `Errors` = erro ou
timeout; `FailedWithoutDetail` = falhas antigas, de antes do #43, sem separar errado de erro.

```sql
WITH visible_lessons AS (
    SELECT l."Id" AS lesson_id, p."Order" AS path_order, p."Title" AS path_title,
           m."Order" AS module_order, m."Title" AS module_title, l."Order" AS lesson_order, l."Title" AS lesson_title
    FROM "Lessons" l
    JOIN "Modules" m ON m."Id" = l."ModuleId"
    JOIN "CareerPaths" p ON p."Id" = m."CareerPathId"
    WHERE p."IsActive" AND m."IsPublished" AND l."IsPublished" AND NOT l."IsDraft"
)
SELECT vl.lesson_id AS "LessonId", vl.path_title AS "PathTitle", vl.module_order AS "ModuleOrder",
       vl.module_title AS "ModuleTitle", vl.lesson_order AS "LessonOrder", vl.lesson_title AS "LessonTitle",
       COUNT(*)::int AS "Attempts",
       COUNT(DISTINCT s."UserId")::int AS "Students",
       COUNT(*) FILTER (WHERE s."Result" = 'Pass')::int AS "Passed",
       COUNT(*) FILTER (WHERE s."Outcome" = 'Fail')::int AS "Wrong",
       COUNT(*) FILTER (WHERE s."Outcome" IN ('Error', 'Timeout'))::int AS "Errors",
       COUNT(*) FILTER (WHERE s."Result" = 'Fail' AND s."Outcome" IS NULL)::int AS "FailedWithoutDetail",
       ROUND(100.0 * COUNT(*) FILTER (WHERE s."Result" = 'Fail') / COUNT(*), 1) AS "FailureRatePercent"
FROM visible_lessons vl
JOIN "Exercises" e ON e."LessonId" = vl.lesson_id AND e."IsPublished"
JOIN "CodeSubmissions" s ON s."ExerciseId" = e."Id"
GROUP BY vl.lesson_id, vl.path_order, vl.path_title, vl.module_order, vl.module_title, vl.lesson_order, vl.lesson_title
ORDER BY "FailureRatePercent" DESC, vl.path_order, vl.path_title, vl.module_order, vl.lesson_order;
```

## Como dar acesso de admin

Não há tela para isso (de propósito). O papel é o `Admin` do ASP.NET Identity (policy `Admin` na Web). Rode no banco
da aplicação, trocando o e-mail (a pessoa precisa já ter conta):

```sql
-- 1) Cria o papel, se ainda não existir.
INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
SELECT gen_random_uuid(), 'Admin', 'ADMIN', gen_random_uuid()::text
WHERE NOT EXISTS (SELECT 1 FROM "AspNetRoles" WHERE "NormalizedName" = 'ADMIN');

-- 2) Dá o papel para a conta.
INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id"
FROM "AspNetUsers" u, "AspNetRoles" r
WHERE u."NormalizedEmail" = UPPER('dono@exemplo.com') AND r."NormalizedName" = 'ADMIN'
ON CONFLICT DO NOTHING;
```

**Atenção:** `AspNetUsers` tem uma coluna `IsAdmin` (do modelo de usuário da SPEC, desde o #9), mas **nada a
usa**: marcar `IsAdmin = true` não dá acesso. Quem vale é o papel `Admin` acima.

O papel vai no cookie de login: a pessoa precisa **sair e entrar de novo** para ganhar (ou perder) o acesso.
Para tirar: `DELETE FROM "AspNetUserRoles"` com o mesmo `UserId`/`RoleId`.

Quem não é admin: sem login, vai para a tela de login; logado sem o papel, recebe **404** (a área administrativa não
aparece para alunos).
