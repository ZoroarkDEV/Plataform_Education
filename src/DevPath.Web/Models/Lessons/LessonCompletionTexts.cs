namespace DevPath.Web.Models.Lessons;

/// <summary>Textos finais da UX (#27 / #30) para conclusão de aula e "Seguir sem pontos". {N} = pontos do backend.</summary>
public static class LessonCompletionTexts
{
    public const string CompleteButton = "Concluí esta aula";
    public const string Saving = "Salvando…";
    public const string BadgeVisible = "Concluída";
    public const string BadgeCheck = "✓";
    public const string BadgeSrOnly = "Aula concluída";

    public const string NextLabel = "Próxima aula →";
    public const string PrevLabel = "← Aula anterior";
    public const string NextLockedReason = "Conclua esta aula para liberar a próxima";
    public const string ComingSoon = "Em breve";

    public const string ExerciseBlocker = "Resolva o exercício acima para concluir esta aula.";
    public const string SkipButton = "Seguir sem pontos";
    public const string SkipDescribedBy =
        "Travou? Você pode avançar agora e voltar a este exercício depois. Se acertar mais tarde, ganha os pontos.";
    public const string SkipAvailableAnnounce = "Se preferir, você já pode seguir sem pontos.";

    public const string SuccessGeneric = "Boa! Aula concluída. A próxima aula já está liberada.";
    public static string SuccessModule(string module) => $"Boa! Você concluiu o módulo {module}.";
    public static string SuccessPath(string path) => $"Parabéns! Você concluiu a trilha {path}.";

    public const string ReopenReading = "Você já concluiu esta aula. Pode revisar quando quiser.";
    public static string ReopenPassed(int points) => $"Você já concluiu esta aula (+{points} pontos). Pode revisar quando quiser.";
    public static string ReopenSkipped(int points) =>
        $"Você seguiu sem pontos. O exercício continua aberto: acerte quando quiser para ganhar +{points} pontos.";

    public const string SkipJustCompleted = "Tudo certo, aula concluída sem pontos. A próxima aula já está liberada.";
    public static string PassCompletes(int points) =>
        $"Boa! Você acertou o exercício e concluiu a aula: +{points} pontos. A próxima aula já está liberada.";
    public static string PassAfterSkip(int points) => $"Boa! Você acertou o exercício e ganhou +{points} pontos.";

    public const string AttemptsExhaustedJustCompleted =
        "Você usou todas as tentativas deste exercício, então a aula foi concluída sem pontos. A próxima aula já está liberada.";
    public const string AttemptsExhaustedReopen =
        "Você usou todas as tentativas deste exercício, então a aula foi concluída sem pontos. Pode revisar a aula quando quiser.";

    public const string SaveError =
        "Não conseguimos salvar seu progresso. Confira sua conexão e tente de novo.";

    public const string RequirementNotMetFlash = "Para concluir, resolva o exercício acima.";

    public const string LockedTitle = "Aula bloqueada";
    public static string LockedMessage(string requiredTitle) =>
        $"Para liberar, conclua a aula “{requiredTitle}”.";
    public static string GoToRequired(string requiredTitle) => $"Ir para “{requiredTitle}”";
    public const string BackToPath = "Voltar para minha trilha";

    public const string OtherPathTitle = "Aula de outra trilha";
    public static string OtherPathMessage(string currentPathTitle) =>
        $"Esta aula é de outra trilha. Por enquanto é uma trilha por vez: você está em “{currentPathTitle}”.";

    // Exercício / Executar (#30 B2)
    public const string ExecuteButton = "Executar";
    public const string ExecuteLoading = "Executando…";
    public const string AttemptsExhaustedReason = "Você usou todas as tentativas deste exercício.";
    public static string RemainingAttempts(int n) => $"Tentativas restantes: {n}";
    public const string LastAttempt = "Esta é sua última tentativa.";

    public const string InstructionUnlimited =
        "Escreva sua consulta no editor abaixo e clique em **Executar** para ver o resultado. Pode executar quantas vezes quiser: errar não tira pontos. Se travar, abra as dicas.";
    public const string InstructionUnlimitedNoHints =
        "Escreva sua consulta no editor abaixo e clique em **Executar** para ver o resultado. Pode executar quantas vezes quiser: errar não tira pontos.";
    public const string InstructionLimited =
        "Escreva sua consulta no editor abaixo e clique em **Executar** para ver o resultado. Cada execução conta como tentativa. Se travar, abra as dicas.";
    public const string InstructionLimitedNoHints =
        "Escreva sua consulta no editor abaixo e clique em **Executar** para ver o resultado. Cada execução conta como tentativa.";

    public const string EmptyCodeClient = "Escreva sua consulta antes de executar.";
    public const string CodeTooLarge = "Sua consulta está grande demais para executar aqui.";
    public const string RateLimited =
        "Calma, muitas execuções seguidas. Espere alguns segundos e tente de novo. Isso não contou como tentativa.";
    public const string PlatformError =
        "O exercício está com um problema do nosso lado. Tente de novo em instantes. Isso não contou como tentativa.";
    public const string ExerciseNotFoundFriendly =
        "Não encontramos este exercício. Ele pode ter sido despublicado. Volte ao dashboard e escolha outra aula.";
    public const string UnauthorizedFriendly =
        "Sua sessão expirou. Entre de novo para continuar.";
}
