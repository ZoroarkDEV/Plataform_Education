# ADR-007 — Progresso do aluno: concluir aula, desbloqueio e "Continuar"

Status: Proposed
Data: 2026-10-29
Issues: #27 (escopo principal), #11 (fecha junto), #25 (checagem de matrícula na execução), #30 (B1 e B3 da auditoria de UX)

> Esta é a **proposta de contrato**. Nada aqui está implementado ainda. O ponto de partida é a proposta do
> UI Operator; cada mudança em relação a ela está na seção "Diferenças para a proposta do UI", com o motivo.

## Contexto

- Hoje nada grava `UserProgress`: `UserProgress.StartLesson` e `Complete` não são chamados em lugar nenhum.
  O dashboard fica em 0% e as aulas depois da 1ª ficam "Bloqueada" para sempre (#30 B1).
- A regra de desbloqueio existe **só** no dashboard (`JourneyCalculator`, R1/R4/R5). A página da aula, o
  "Próxima aula →" e o `POST /api/exercicios/{id}/executar` não a aplicam: uma aula "Bloqueada" abre pela
  paginação ou pela URL (#30 B3), e qualquer aluno logado executa exercício de qualquer trilha (#25, QA).
- `UserPathEnrollments.ProgressPercentage` e `CurrentModuleId` são gravadas só na matrícula e ficam
  enganosas depois (#27). Nenhuma tela as lê: o dashboard calcula na hora.
- O "Continuar" aponta para a 1ª aula liberada e não concluída; o Manager decidiu (#27) que deve ir para a
  aula **em andamento**.
- Decisões do Manager (Ibra), provisórias, 29/10:
  - a execução exige **matrícula ativa** na trilha da aula;
  - regra de conclusão: aula com exercício conclui com uma submissão `pass` (ganha os pontos do exercício,
    hoje 10) **ou** com as tentativas esgotadas (conclui com 0 ponto, e o estado diz isso para a UI sugerir
    revisar a aula); aula só de leitura conclui com o clique; senão `RequirementNotMet` com o motivo;
  - aula bloqueada não abre, nem pelo "Próxima aula →" nem pela URL. O servidor aplica **exatamente** a regra
    que o dashboard mostra (uma fonte de verdade só).

## Decisão

### 1. Uma fonte de verdade para o desbloqueio

`JourneyCalculator.Build` (função pura, Application, já testada em unidade) continua sendo **a** regra. Ela passa a
ser chamada por todos os pontos que decidem acesso, sempre com os mesmos dados (roadmap da trilha + progresso do aluno):

| Quem | Usa para |
|---|---|
| `IStudentJourneyService.GetDashboardAsync` | status de cada aula, %, "Continuar" (como hoje) |
| `ILessonProgressService.OpenAsync` / `GetStateAsync` | abrir ou não a página da aula; estado de "Anterior"/"Próxima" |
| `ILessonProgressService.CompleteAsync` | recusar concluir aula bloqueada |
| `ILessonAccessPolicy.EvaluateAsync` | `POST /api/exercicios/{id}/executar` |

Na Infrastructure, um único carregador interno (`JourneySnapshotLoader`) faz as duas consultas que o dashboard já
faz hoje (roadmap da trilha e progresso do aluno nas aulas dela) e chama o `JourneyCalculator`. O dashboard passa a
usá-lo também, para não existir uma segunda cópia da consulta.

**Regra exata (inalterada, só documentada aqui):**
- Aula **disponível** = publicada, de módulo publicado, trilha ativa e **não** rascunho.
- Rascunho = `ComingSoon`: não abre, não conta no %, **nunca bloqueia** (é pulado ao procurar "a aula anterior").
- A 1ª aula disponível da trilha está sempre liberada.
- Dentro do módulo, uma aula exige a aula disponível **anterior do mesmo módulo** concluída.
- A 1ª aula disponível de um módulo exige **todas** as aulas disponíveis do módulo anterior que tem aulas
  disponíveis concluídas. O requisito mostrado é a última aula não concluída desse módulo.
- Concluída = `Completed` ou `Mastered`.
- R5: aula `Completed` ou `InProgress` nunca aparece bloqueada, mesmo se o conteúdo mudar (ex.: aula nova
  inserida antes dela). Como `InProgress` só é gravado quando a aula estava liberada (seção 3), isso não abre brecha.
- Acesso a uma aula = aluno com **matrícula ativa** na trilha da aula (`UserPathEnrollments.IsActive` e
  `AspNetUsers.CurrentPathId` = trilha) **e** status da aula ∈ {`Completed`, `InProgress`, `Available`}.

### 2. Regra de conclusão (uma função só)

`LessonCompletionRule.Evaluate(...)`, função pura na Application, testada em unidade. Se o Manager mudar a regra,
muda só ela. Entrada: se a aula tem exercício e, para cada exercício da aula, `Passed`, `AttemptsUsed`, `MaxAttempts`
e `Points`.

| Situação | Resultado |
|---|---|
| Aula sem exercício (só leitura) | pode concluir; `CompletionKind = Read`, `Points = null` |
| Todo exercício da aula tem uma submissão `Pass` | pode concluir; `ExercisePassed`, `Points = soma dos Points` |
| Algum exercício sem `Pass` e com `MaxAttempts` atingido (os demais com `Pass` ou também esgotados) | pode concluir; `AttemptsExhausted`, `Points = soma só dos que passaram` (0 com um exercício só) |
| Algum exercício sem `Pass` e com tentativas sobrando (ou sem limite) | **não** pode; `Blocker = ExerciseNotPassed` |

Hoje há um exercício por aula (ADR-003); a regra já trata "cada exercício da aula" para não quebrar se vierem mais.

**Proposta: `pass` conclui a aula sozinho** (questão aberta Q1). O `ExerciseSubmissionService` (Worker), ao gravar
uma submissão `Pass`, faz o mesmo upsert de conclusão (`ExercisePassed`, pontos) **na mesma transação** da
submissão. Motivos: o aluno não "esquece de clicar" depois de acertar, o próximo passo já aparece liberado e a #30
B1 recomenda exatamente isso ("a conclusão passa a vir do envio aprovado do exercício"). O clique continua
existindo para aula só de leitura e para tentativas esgotadas (aí o aluno escolhe seguir sem pontos). O
`ExecutionOutcome` (ADR-006) ganha um campo aditivo `lessonCompleted: bool` para o JS atualizar o bloco de conclusão.
Se o Manager preferir o clique sempre, basta não chamar o upsert no Worker: `CanComplete` vira `true` depois do `pass`.

### 3. O que registra "em andamento" — e a migration

Reaproveita a linha de `UserProgress` por aula (SPEC §4), sem coluna `StartedAt` nova:
- **Abrir a página da aula** liberada (`OpenAsync`) faz upsert: sem linha → cria `InProgress` com `LastAccessedAt = agora`;
  com linha → só atualiza `LastAccessedAt` (aula concluída **continua** concluída ao ser reaberta).
- Aula bloqueada, de outra trilha ou rascunho **não** grava nada.
- **"Continuar"** = a aula `InProgress` com o `LastAccessedAt` mais recente (a última que o aluno abriu e não
  concluiu); se não houver, a 1ª aula `Available` na ordem da trilha; `null` se tudo disponível está concluído.
  `LastAccessedAt` responde melhor "onde ele parou" do que "a primeira que começou" (Q6).

Migration **`AddLessonProgressTracking`** (data depois de `20261029122848_AddDatasets`):
1. Remove duplicatas de `UserProgress` por `(UserId, LessonId)` mantendo o status mais avançado
   (`CASE Status WHEN 'Mastered' THEN 3 WHEN 'Completed' THEN 2 WHEN 'InProgress' THEN 1 ELSE 0 END`, desempate pelo
   `LastAccessedAt` mais novo). Hoje ninguém grava progresso, então em dev isso não apaga nada.
2. Troca o índice não único `(UserId, LessonId)` por **único parcial** `IX_UserProgress_UserId_LessonId`
   `WHERE "LessonId" IS NOT NULL`. É ele que deixa o upsert (`INSERT ... ON CONFLICT DO UPDATE`) idempotente e
   seguro com duplo clique / duas abas.
3. Coluna nova `UserProgress.CompletionKind` (`varchar(20)`, nula; `Read | ExercisePassed | AttemptsExhausted`) com
   `CK_UserProgress_CompletionKind` = `"CompletionKind" IS NULL OR "Status" IN ('Completed','Mastered')`.
   Os pontos ficam em `Score`, que já existe (null em aula de leitura, 0 em tentativas esgotadas).

O `GroupBy(...).Max(Status)` do dashboard pode ficar (inofensivo), mas deixa de ser necessário.

### 4. `ProgressPercentage` e `CurrentModuleId` da matrícula: **mantidas e recalculadas**

Mantidas porque estão no modelo de dados da SPEC §4 (`UserPathEnrollment`), e a ordem de autoridade é
README > SPEC > código. Mas ficam explicitamente como **cópia para relatório**: nenhuma tela lê essas colunas; a
fonte de verdade das telas é sempre o cálculo na hora (`JourneyCalculator`).

- `ProgressPercentage` = o mesmo % do dashboard (concluídas × 100 ÷ disponíveis, truncado), gravado como inteiro
  na coluna `decimal(5,2)`.
- `CurrentModuleId` = módulo da 1ª aula disponível **não concluída** na ordem da trilha (a "fronteira" do aluno; não
  muda só por abrir uma aula). Tudo concluído → o último módulo com aulas disponíveis.
- Recalculadas, **na mesma transação**, em: `CompleteAsync`, conclusão automática do Worker e `EnrollAsync` quando
  **reativa** uma matrícula antiga (item do #27). Para duas conclusões simultâneas não gravarem um % velho, a
  transação começa com `SELECT ... FOR UPDATE` na linha da matrícula ativa (serializa por aluno).
- Conhecido: quando o seed publica/despublica aulas, o denominador muda e as colunas só se corrigem na próxima
  conclusão do aluno (Q7). As telas não são afetadas.
- `UserPathEnrollment.UpdateProgress(decimal, Guid?)` continua sendo o único jeito de mudar as duas colunas.

### 5. Contrato da Application (`DevPath.Application.Progress`)

`userId` é **`Guid`** em todo o código existente (`ApplicationUser : IdentityUser<Guid>`, `IStudentJourneyService`,
`ClaimsPrincipalExtensions.TryGetUserId(out Guid)`, endpoint de execução). Mantido.

```csharp
namespace DevPath.Application.Progress;

/// Progresso do aluno numa aula (#27). Nunca lança para os casos esperados (mesmo estilo do IStudentJourneyService).
public interface ILessonProgressService
{
    /// Só leitura. Para o bloco de conclusão (HTMX) e para a paginação.
    Task<LessonAccessResult> GetStateAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default);

    /// O mesmo que GetStateAsync e, se Kind = Allowed, registra a abertura (InProgress / LastAccessedAt, seção 3).
    /// Chamado pelo GET da página da aula.
    Task<LessonAccessResult> OpenAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default);

    /// Conclui a aula (regra da seção 2) e recalcula a matrícula na mesma transação. Idempotente.
    Task<LessonCompletionResult> CompleteAsync(Guid userId, string pathSlug, string moduleSlug, string lessonSlug, CancellationToken ct = default);
}

public enum LessonAccessKind
{
    Allowed,         // matriculado e aula liberada (Completed | InProgress | Available)
    Locked,          // matriculado, aula bloqueada: State vem preenchido, com UnlockRequirement
    NotEnrolled,     // aula existe, mas não é da trilha atual (ou o aluno não tem trilha): State = null
    LessonNotFound,  // não existe, escondida (trilha inativa, módulo/aula não publicados) ou rascunho
    UserNotFound     // conta apagada com cookie válido
}
// Ordem de verificação: UserNotFound > LessonNotFound > NotEnrolled > Locked > Allowed.

/// <param name="State">Preenchido em Allowed e Locked.</param>
/// <param name="CurrentPathTitle">Só em NotEnrolled: título da trilha atual do aluno; null se ele não tem trilha.</param>
public sealed record LessonAccessResult(LessonAccessKind Kind, LessonProgressState? State = null, string? CurrentPathTitle = null);

public sealed record LessonProgressState(
    LessonRefDto Lesson,                        // o mesmo LessonRefDto do Journey
    JourneyLessonStatus Status,                 // Completed | InProgress | Available | Locked (nunca ComingSoon)
    LessonRefDto? UnlockRequirement,            // só em Locked: a aula que falta concluir (igual ao dashboard)
    DateTimeOffset? CompletedAt,
    LessonCompletionKind? CompletionKind,       // null se não concluída
    int? Points,                                // pontos ganhos; null se não concluída ou aula só de leitura
    bool CanComplete,                           // Status != Locked && não concluída && regra da seção 2 permite
    LessonCompletionBlocker? Blocker,           // por que CanComplete = false numa aula liberada e não concluída
    LessonExerciseProgressDto? Exercise,        // null = aula só de leitura
    DashboardLessonDto? Previous,               // vizinhas na ordem da TRILHA (cruza módulos), rascunhos incluídos
    DashboardLessonDto? Next,                   //   (Status ComingSoon); Locked traz UnlockRequirement
    int PathProgressPercentage)                 // % da trilha depois desta ação (mesmo número do dashboard)
{
    public bool IsCompleted => Status == JourneyLessonStatus.Completed;
}

public enum LessonCompletionKind { Read, ExercisePassed, AttemptsExhausted }

public enum LessonCompletionBlocker { ExerciseNotPassed }

public sealed record LessonExerciseProgressDto(
    Guid ExerciseId, int Points, bool Passed, int AttemptsUsed, int? MaxAttempts, int? RemainingAttempts)
{
    public bool AttemptsExhausted => !Passed && MaxAttempts is { } max && AttemptsUsed >= max;
}

public enum LessonCompletionResultKind
{
    Completed,          // concluiu agora
    AlreadyCompleted,   // já estava concluída (idempotente, nada muda; 2º POST de duplo clique)
    RequirementNotMet,  // liberada, mas a regra da seção 2 não permite (State.Blocker diz o motivo)
    Locked,             // aula bloqueada (State.UnlockRequirement)
    NotEnrolled,
    LessonNotFound,     // inexistente, escondida ou rascunho
    UserNotFound
}
// Ordem: UserNotFound > LessonNotFound > NotEnrolled > Locked > AlreadyCompleted > RequirementNotMet > Completed.

/// <param name="State">Estado DEPOIS da ação; preenchido em Completed, AlreadyCompleted, RequirementNotMet e Locked.</param>
public sealed record LessonCompletionResult(LessonCompletionResultKind Kind, LessonProgressState? State = null)
{
    public int ProgressPercentage => State?.PathProgressPercentage ?? 0;
}
```

**Execução (`POST /api/exercicios/{id}/executar`):** `ILessonAccessPolicy` vira assíncrona e passa a aplicar a regra
da seção 1:

```csharp
public enum ExerciseAccess { Allowed, NotFound, NotEnrolled, Locked }

public interface ILessonAccessPolicy
{
    Task<ExerciseAccess> EvaluateAsync(Guid userId, Guid exerciseId, CancellationToken ct = default);
}
```

A implementação sai da Application (a atual é pura e síncrona) para a Infrastructure, usando o `IExerciseCatalog` e o
`JourneySnapshotLoader`. O endpoint responde **o mesmo 404 `{ "message": "Exercício não encontrado." }`** para
`NotFound`, `NotEnrolled` e `Locked` (convenção do ADR-006: não revelar qual é o caso) e registra o motivo real em
log `Information`. Nada muda para o Frontend na API, exceto o campo aditivo `lessonCompleted` (seção 2).

**`ExerciseDto` ganha `Guid Id`** (como propriedade `init`, para não quebrar os construtores existentes nem os
fakes de teste da Web). Sem ele a página não tem como chamar `POST /api/exercicios/{exerciseId}/executar`.

**Dashboard (`IStudentJourneyService`):** a interface não muda.
- `DashboardDto.NextLesson` muda de significado (seção 3): última `InProgress` aberta, senão 1ª `Available`.
- `DashboardLessonDto` ganha `LessonCompletionKind? Completion = null` (parâmetro opcional no fim, não quebra
  ninguém) para o roadmap poder marcar "Concluída sem pontos — vale revisar".
- `IsFirstAccess` passa a ficar `false` assim que o aluno **abre** a 1ª aula (abrir grava `InProgress`). O botão
  vira "Continuar" depois da 1ª visita, e não só depois da 1ª conclusão.
- `JourneyCalculator.Build` ganha uma sobrecarga que recebe `JourneyProgressInput(ProgressStatus Status, DateTime
  LastAccessedAt, LessonCompletionKind? CompletionKind)` por aula; a atual continua (os testes de unidade não mudam).

### 6. Web (parte do UI Operator, contrato combinado aqui)

**Página da aula `GET /trilhas/{path}/{module}/{lesson}`.** O `LessonsController.Show` chama
`ILessonContentService.GetLessonAsync` (inalterado) e depois `ILessonProgressService.OpenAsync`. Ordem final:
`NotFound` > `Draft` > **acesso** > `Malformed` (aluno sem acesso não vê um 500 de aula com defeito).

| `LessonAccessKind` | Página |
|---|---|
| `Allowed` | abre normalmente; paginação e bloco de conclusão vêm de `State` |
| `Locked` | **view `Unavailable` com motivo novo `Locked`, HTTP 403** (proposta, Q4): título "Aula bloqueada", texto "Para liberar, conclua a aula “{UnlockRequirement.Title}”.", botão principal "Ir para “{X}”" (link só se X estiver liberada; senão texto, como no dashboard) e secundário "Voltar para minha trilha". Não redireciona: a URL continua a que o aluno pediu (favorito/link compartilhado explica o que fazer), e não há redirect em cadeia. É a opção (a) da #30 B3. |
| `NotEnrolled`, `CurrentPathTitle = null` | redirect para `/escolha-sua-trilha` (mesmo comportamento do dashboard sem trilha) |
| `NotEnrolled`, com trilha | view `Unavailable`, motivo novo `OtherPath`, HTTP 403: "Esta aula é de outra trilha. Por enquanto é uma trilha por vez: você está em “{CurrentPathTitle}”." + "Voltar para minha trilha" |
| `LessonNotFound` | o 404 atual (só acontece numa corrida com o seed) |
| `UserNotFound` | logout e `/Account/Login` (mesmo tratamento do onboarding) |

**Paginação ("← Aula anterior" / "Próxima aula →").** Passa a usar `State.Previous` / `State.Next`
(`DashboardLessonDto`, na ordem da trilha, cruzando módulos) em vez de `LessonDto.PreviousLesson/NextLesson`:
- `Completed | InProgress | Available` → link (como hoje).
- `ComingSoon` → "Em breve", sem link (como hoje).
- **`Locked` → sem link**, `aria-disabled="true"`: "Próxima aula (bloqueada)", título da aula e
  "Para liberar, conclua a aula “{UnlockRequirement.Title}”." (com a aula atual não concluída, o requisito é ela mesma:
  "Para liberar, conclua esta aula."). Depois de concluir via HTMX, a resposta troca a paginação também (`hx-swap-oob`),
  e a próxima vira link.

**Concluir: `POST /trilhas/{path}/{module}/{lesson}/concluir`**
- `[Authorize]` + `[ValidateAntiForgeryToken]` (o `<form>` Razor já leva `__RequestVerificationToken`; via HTMX vale
  também o header `RequestVerificationToken`). Token inválido → 400 (padrão do MVC).
- Com `HX-Request: true`:
  - `Completed`, `AlreadyCompleted`, `RequirementNotMet` → **200** com a partial `_LessonCompletion` (estado + mensagem
    com `role="status"`), mais a paginação com `hx-swap-oob`. Em `Completed`, header
    `HX-Trigger: {"lessonCompleted":{"progressPercentage":N}}`.
  - `Locked`, `NotEnrolled`, `LessonNotFound` → `HX-Redirect` para a URL da aula (o GET mostra a tela certa da tabela acima).
  - `UserNotFound` → logout + `HX-Redirect: /Account/Login`.
- Sem HTMX: **PRG**, `303 See Other` para a URL da aula, com flash (TempData). Textos propostos:
  - `Completed` / `Read` ou `ExercisePassed`: "Aula concluída! A próxima já está liberada." (+ "Você ganhou N pontos." quando houver pontos);
  - `Completed` / `AttemptsExhausted`: "Aula concluída sem pontos. Vale revisar a aula antes de seguir.";
  - `AlreadyCompleted`: sem flash;
  - `RequirementNotMet`: "Para concluir, faça o exercício passar nos casos de teste.".
  Os casos `Locked`/`NotEnrolled`/`LessonNotFound` caem na mesma tela do GET.

**Atualizar o bloco: `GET /trilhas/{path}/{module}/{lesson}/conclusao`** → partial `_LessonCompletion` (só leitura,
`GetStateAsync`). O JS do exercício chama depois de um `pass` com `lessonCompleted: true`. Sem acesso → 404 vazio.

**O que o bloco `_LessonCompletion` mostra (por estado):**
- Concluída: "Concluída em {data}" + pontos, ou "Concluída sem pontos — vale revisar a aula" (`AttemptsExhausted`).
- `CanComplete` e aula de leitura: botão principal "Marcar aula como concluída".
- `CanComplete` e tentativas esgotadas: botão "Concluir sem pontos e seguir".
- `Blocker = ExerciseNotPassed`: sem botão; "Faça o exercício passar para concluir a aula." (+ tentativas restantes se houver limite).

## Diferenças para a proposta do UI (e por quê)

1. **`userId` tipado como `Guid`**: é o tipo usado em todo o Identity/Journey.
2. **`LessonRef` → `LessonRefDto`**: é o nome do record no backend (`DevPath.Application.Journey`); a Web continua
   mapeando para o seu `LessonRef`.
3. **`GetStateAsync` devolve `LessonAccessResult` (Kind + State)** em vez de `LessonProgressState` direto, e saiu
   `IsEnrolled`: a página precisa distinguir bloqueada, de outra trilha, inexistente e conta apagada (#30 B3), e um
   bool só não basta. O estado só existe quando há matrícula.
4. **Novo `OpenAsync`**: o GET da aula precisa registrar "em andamento" para o "Continuar"; `GetStateAsync` fica sem
   efeito colateral (seguro para o refresh HTMX).
5. **`Status` + `UnlockRequirement` no estado**: a proposta não dizia se a própria aula está bloqueada.
6. **`CannotCompleteReason` (string) → `Blocker` (enum)** e **`NextUnlocked`/`NextLockedReason` → `Next`/`Previous`
   como `DashboardLessonDto`**: segue a convenção do projeto (a Web escolhe o texto pelo Kind, como em
   `LessonLoadErrorKind`) e reaproveita o DTO e o `UnlockRequirement` que o dashboard já usa (mesma regra, mesmo
   formato). `Previous` entra porque, com R5, a anterior também pode não estar liberada.
7. **`CompletionKind`, `Points` e `Exercise`** no estado: exigência da regra do Manager (concluída sem pontos,
   tentativas restantes, `ExerciseId` para executar).
8. **Enum do resultado renomeado `LessonCompletionResultKind`** (padrão de `EnrollmentResultKind`), liberando
   `LessonCompletionKind` para "como concluiu" (`Read | ExercisePassed | AttemptsExhausted`). Ganhou **`Locked`**,
   separado de `RequirementNotMet`: são motivos e textos diferentes (aula anterior × exercício).
9. **`ProgressPercentage` do resultado** vira propriedade derivada do estado (`PathProgressPercentage`), o mesmo
   número inteiro do dashboard, e não o `decimal` da matrícula.
10. **Matrícula/`CurrentModuleId`**: mantido o "atualiza na mesma transação" da proposta, com a definição exata e o
    lock por aluno da seção 4.
11. **Rota de refresh `GET .../conclusao`** e campo `lessonCompleted` na execução: necessários para a conclusão
    automática no `pass` (Q1).

## Consequências

- Dashboard, página da aula, paginação, conclusão e execução usam a mesma regra (`JourneyCalculator`) com os mesmos
  dados (`JourneySnapshotLoader`). Fecha a #30 B3 e a checagem de matrícula do #25.
- Abrir uma aula passa a escrever no banco (1 upsert por GET). Aceitável no MVP; se pesar, dá para só atualizar
  `LastAccessedAt` quando a diferença for maior que N minutos.
- A página da aula passa de 3 para 5 consultas (+ roadmap e progresso). Sem N+1.
- Uma migration (`AddLessonProgressTracking`). Quem tiver linhas duplicadas de progresso perde as menos avançadas.
- `ILessonAccessPolicy` muda de assinatura (síncrona → assíncrona); só o endpoint de execução e os testes dele usam.
- **Risco de produto (Q2):** todas as aulas com exercício hoje têm `MaxAttempts` nulo (sem limite) e não há tela que
  chame a execução (#30 B1/B2). Com a regra do Manager, essas aulas **só concluem com `pass`**, e o aluno só consegue
  um `pass` quando a UI tiver o "Executar".
- Testes previstos: unidade (`LessonCompletionRule`, nova sobrecarga do `JourneyCalculator`, "Continuar"); integração
  com Postgres real (concluir aula 1 → aula 2 liberada no dashboard e na página; URL direta de aula bloqueada → 403;
  aula de outra trilha → 403; execução sem matrícula / bloqueada → 404; duplo POST de concluir → 1 linha;
  conclusões concorrentes → % certo; reativar matrícula recalcula; `pass` conclui sozinho).

## Questões abertas

- **Q1 (Ibra):** `pass` conclui a aula sozinho (proposta) ou o aluno ainda clica?
- **Q2 (Ibra, bloqueia a entrega útil):** nenhuma aula com exercício pode ser concluída enquanto a UI não tiver o
  "Executar" (o endpoint já existe). Opções: (a) a UI entrega o "Executar" junto com o #27; (b) regra provisória:
  enquanto não houver execução na tela, aula com exercício conclui com o clique (`Read`, sem pontos); (c) aceitar
  aulas travadas até o #14.
- **Q3 (Ibra):** exercício sem limite de tentativas nunca "esgota": quem não acerta fica preso. Aceitar (dicas
  bastam) ou liberar "seguir sem pontos" depois de N falhas?
- **Q4 (Ibra/UI):** aula bloqueada e de outra trilha com **403 e a view `Unavailable`** (proposta) ou 404 como o
  rascunho? A API continua 404 em todos os casos.
- **Q5 (Ibra):** aula só de leitura vale pontos? Proposta: não (`Points = null`).
- **Q6:** "Continuar" pelo `LastAccessedAt` (última aberta, proposta) ou pela primeira iniciada?
- **Q7:** o seed deveria recalcular `ProgressPercentage`/`CurrentModuleId` de todas as matrículas quando muda o
  conteúdo? Proposta: follow-up, porque nenhuma tela lê essas colunas.
- **Q8 (UI):** paginação cruzando módulos (proposta) ou só dentro do módulo, como hoje?
- **Q9 (UI):** o roadmap vai usar `DashboardLessonDto.Completion` ("Concluída sem pontos")?
