using FluentAssertions;
using DevPath.Application.Lessons;
using DevPath.Domain.Common;
using DevPath.Web.Models.Lessons;
using DevPath.Web.Progress;
using DevPath.Web.ViewModels.Onboarding;

namespace DevPath.UnitTests.Web.Lessons;

public sealed class LessonCompletionViewModelTests
{
    private static readonly LessonRef Select = new("sql-dados", "primeiros-passos", "seu-primeiro-select", "Seu primeiro SELECT");
    private static readonly LessonRef Where = new("sql-dados", "primeiros-passos", "filtrando-com-where", "Filtrando com WHERE");
    private static readonly Guid ExerciseId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static readonly LessonDto Reading = new(
        Guid.NewGuid(), "seu-primeiro-select", "Seu primeiro SELECT", 15, DifficultyLevel.Beginner, null,
        ["Obj"], "# body",
        new LessonBreadcrumb("sql-dados", "SQL & Dados", "primeiros-passos", "Primeiros passos"),
        null, new LessonLink("filtrando-com-where", "Filtrando com WHERE"),
        Position: 1, TotalInModule: 4);

    private static LessonItemViewModel Available(LessonRef lesson) => new(lesson, LessonStatus.Available);
    private static LessonItemViewModel Locked(LessonRef lesson, LessonRef req) => new(lesson, LessonStatus.Locked, req);

    private static LessonProgressView ReadingAvailable(LessonItemViewModel? next = null) => new(
        Select, LessonStatus.Available, null, null, null, null,
        CanComplete: true, Blocker: null, Exercise: null,
        Previous: null, Next: next ?? Available(Where), PathProgressPercentage: 0);

    private static LessonProgressView ReadingCompleted(LessonItemViewModel? next = null) => new(
        Select, LessonStatus.Completed, null, DateTimeOffset.UtcNow,
        LessonCompletionKind.Read, null, CanComplete: false, Blocker: null, Exercise: null,
        Previous: null, Next: next ?? Available(Where), PathProgressPercentage: 20);

    private static LessonProgressView ExerciseNotPassed(int failed = 0, bool canSkip = false) => new(
        Select, LessonStatus.Available, null, null, null, null,
        CanComplete: false, Blocker: LessonCompletionBlocker.ExerciseNotPassed,
        Exercise: new LessonExerciseProgressView(ExerciseId, 10, false, failed, null, null, failed, canSkip,
            canSkip ? 0 : Math.Max(0, 5 - failed)),
        Previous: null, Next: Locked(Where, Select), PathProgressPercentage: 0);

    private static LessonProgressView ExercisePassed(int points = 10) => new(
        Select, LessonStatus.Completed, null, DateTimeOffset.UtcNow,
        LessonCompletionKind.ExercisePassed, points, CanComplete: false, Blocker: null,
        Exercise: new LessonExerciseProgressView(ExerciseId, points, true, 1, null, null, 0, false, null),
        Previous: null, Next: Available(Where), PathProgressPercentage: 20);

    private static LessonProgressView Skipped(int points = 10) => new(
        Select, LessonStatus.Completed, null, DateTimeOffset.UtcNow,
        LessonCompletionKind.SkippedAfterFailures, 0, CanComplete: false, Blocker: null,
        Exercise: new LessonExerciseProgressView(ExerciseId, points, false, 5, null, null, 5, true, 0),
        Previous: null, Next: Available(Where), PathProgressPercentage: 20);

    private static LessonCompletionViewModel Vm(LessonProgressView state, bool justCompleted = false, bool justSkipped = false) =>
        LessonCompletionViewModel.From(state, Reading, justCompleted: justCompleted, justSkipped: justSkipped);

    [Fact]
    public void Reading_available_shows_complete_button()
    {
        var vm = Vm(ReadingAvailable());
        vm.ShowButton.Should().BeTrue();
        vm.ShowBadge.Should().BeFalse();
        vm.ShowSkipButton.Should().BeFalse();
        LessonCompletionTexts.CompleteButton.Should().Be("Concluí esta aula");
    }

    [Fact]
    public void Reading_completed_shows_badge_and_reopen_text()
    {
        var vm = Vm(ReadingCompleted());
        vm.ShowBadge.Should().BeTrue();
        vm.ShowButton.Should().BeFalse();
        vm.SuccessMessage.Should().Be(LessonCompletionTexts.ReopenReading);
    }

    [Fact]
    public void Just_completed_reading_generic_success()
    {
        Vm(ReadingCompleted(), justCompleted: true).SuccessMessage.Should().Be(LessonCompletionTexts.SuccessGeneric);
    }

    [Fact]
    public void Last_of_module_uses_module_message()
    {
        var next = Available(new LessonRef("sql-dados", "outro-modulo", "aula", "Outra"));
        Vm(ReadingCompleted(next), justCompleted: true).SuccessMessage
            .Should().Be(LessonCompletionTexts.SuccessModule("Primeiros passos"));
    }

    [Fact]
    public void Last_of_path_uses_path_message()
    {
        var state = ReadingCompleted() with { Next = null };
        Vm(state, justCompleted: true).SuccessMessage.Should().Be(LessonCompletionTexts.SuccessPath("SQL & Dados"));
    }

    [Fact]
    public void Exercise_not_passed_locks_button_with_reason()
    {
        var vm = Vm(ExerciseNotPassed(failed: 2));
        vm.ShowDisabledExerciseButton.Should().BeTrue();
        vm.ShowButton.Should().BeFalse();
        vm.ShowSkipButton.Should().BeFalse();
        LessonCompletionTexts.ExerciseBlocker.Should().Be("Resolva o exercício acima para concluir esta aula.");
    }

    [Fact]
    public void CanSkip_shows_skip_without_countdown_text()
    {
        var vm = Vm(ExerciseNotPassed(failed: 5, canSkip: true));
        vm.ShowSkipButton.Should().BeTrue();
        vm.ShowDisabledExerciseButton.Should().BeTrue();
        LessonCompletionTexts.SkipButton.Should().Be("Seguir sem pontos");
        LessonCompletionTexts.SkipDescribedBy.Should().Contain("Travou?");
    }

    [Fact]
    public void Just_skipped_and_reopen_messages()
    {
        Vm(Skipped(), justSkipped: true, justCompleted: true).SuccessMessage
            .Should().Be(LessonCompletionTexts.SkipJustCompleted);
        Vm(Skipped()).SuccessMessage.Should().Be(LessonCompletionTexts.ReopenSkipped(10));
        Vm(Skipped()).SuccessMessage.Should().NotContain("+0");
    }

    [Fact]
    public void Pass_completed_shows_points_from_backend()
    {
        Vm(ExercisePassed(7), justCompleted: true).SuccessMessage.Should().Be(LessonCompletionTexts.PassCompletes(7));
        Vm(ExercisePassed(7)).SuccessMessage.Should().Be(LessonCompletionTexts.ReopenPassed(7));
    }

    [Fact]
    public void Never_shows_plus_zero()
    {
        Vm(ExercisePassed(0), justCompleted: true).SuccessMessage.Should().NotContain("+0");
    }

    [Fact]
    public void From_uses_backend_DatasetDisplayName_and_Id()
    {
        var id = Guid.NewGuid();
        var dto = new DevPath.Application.Lessons.ExerciseDto("t", "x", "sql", "loja_online", null, [])
        {
            Id = id,
            DatasetDisplayName = "Loja online",
        };

        var vm = LessonExerciseViewModel.From(dto, new DevPath.Web.Lessons.LessonMarkdownRenderer());

        vm.DatasetDisplayName.Should().Be("Loja online");
        vm.DatasetSlug.Should().Be("loja_online");
        vm.ExerciseId.Should().Be(id);
    }

    [Fact]
    public void Instruction_depends_on_MaxAttempts_and_hints()
    {
        var unlimited = new LessonExerciseViewModel("t", "<p>", "SQL", "livraria", "Livraria", 10, null, "SELECT 1", ["dica"]);
        unlimited.InstructionText.Should().Contain("Pode executar quantas vezes quiser").And.Contain("abra as dicas");

        (unlimited with { Hints = Array.Empty<string>() }).InstructionText.Should().NotContain("abra as dicas");

        var limited = unlimited with { MaxAttempts = 3, RemainingAttempts = 2 };
        limited.InstructionText.Should().Contain("Cada execução conta como tentativa");
        (limited with { RemainingAttempts = 0 }).AttemptsExhausted.Should().BeTrue();
    }

    private static LessonProgressView Exhausted() => new(
        Select, LessonStatus.Completed, null, DateTimeOffset.UtcNow,
        LessonCompletionKind.AttemptsExhausted, 0, CanComplete: false, Blocker: null,
        Exercise: new LessonExerciseProgressView(ExerciseId, 10, false, 3, 3, 0, 3, false, 2),
        Previous: null, Next: Available(Where), PathProgressPercentage: 20);

    [Fact]
    public void Pass_after_skip_says_ganhou_pontos_and_only_announces()
    {
        var vm = LessonCompletionViewModel.AfterPass(ExercisePassed(10), Reading, LessonCompletionKind.SkippedAfterFailures);
        vm.SuccessMessage.Should().Be(LessonCompletionTexts.PassAfterSkip(10));
        vm.SuccessMessage.Should().Be("Boa! Você acertou o exercício e ganhou +10 pontos.");
        vm.MovesFocus.Should().BeFalse();
        vm.Announces.Should().BeTrue();
    }

    [Fact]
    public void Pass_without_previous_skip_uses_pass_message_without_focus()
    {
        var vm = LessonCompletionViewModel.AfterPass(ExercisePassed(7), Reading, null);
        vm.SuccessMessage.Should().Be(LessonCompletionTexts.PassCompletes(7));
        vm.MovesFocus.Should().BeFalse();
        LessonCompletionViewModel.AfterPass(ExercisePassed(7), Reading, LessonCompletionKind.ExercisePassed)
            .PassedAfterSkip.Should().BeFalse();
    }

    [Fact]
    public void Attempts_exhausted_has_own_messages_not_skip()
    {
        Vm(Exhausted(), justCompleted: true).SuccessMessage.Should().Be(
            "Você usou todas as tentativas deste exercício, então a aula foi concluída sem pontos. A próxima aula já está liberada.");
        Vm(Exhausted()).SuccessMessage.Should().Be(
            "Você usou todas as tentativas deste exercício, então a aula foi concluída sem pontos. Pode revisar a aula quando quiser.");
        Vm(Exhausted()).SuccessMessage.Should().NotBe(Vm(Skipped()).SuccessMessage);
    }

    [Fact]
    public void Attempts_exhausted_via_executar_conclusao_usa_texto_do_momento()
    {
        // GET /conclusao depois do último Executar: AfterPass → JustPassed, sem JustCompleted.
        var vm = LessonCompletionViewModel.AfterPass(Exhausted(), Reading, previousKind: null);
        vm.JustPassed.Should().BeTrue();
        vm.JustCompleted.Should().BeFalse();
        vm.MovesFocus.Should().BeFalse("pass pelo Executar só anuncia");
        vm.Announces.Should().BeTrue();
        vm.SuccessMessage.Should().Be(LessonCompletionTexts.AttemptsExhaustedJustCompleted);
        vm.SuccessMessage.Should().NotBe(LessonCompletionTexts.AttemptsExhaustedReopen);
    }

    [Fact]
    public void Only_click_moves_focus()
    {
        Vm(ReadingCompleted(), justCompleted: true).MovesFocus.Should().BeTrue();
        Vm(Skipped(), justSkipped: true).MovesFocus.Should().BeTrue();
        Vm(ReadingCompleted()).MovesFocus.Should().BeFalse();
        Vm(ReadingCompleted()).Announces.Should().BeFalse();
    }
}
