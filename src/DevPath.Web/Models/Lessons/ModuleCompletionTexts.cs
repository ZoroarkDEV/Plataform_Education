namespace DevPath.Web.Models.Lessons;

/// <summary>
/// Textos da tela "Módulo concluído" (#42; PO: roteiro-modulo2-sql.md, "Telas Módulo concluído" e US-M.1).
/// Pontos e % sempre vêm do servidor.
/// </summary>
public static class ModuleCompletionTexts
{
    public static string Title(string moduleTitle) => $"Módulo concluído: {moduleTitle}";

    /// <summary>Fica fora do título (aria-hidden): o leitor de tela não lê o nome do emoji.</summary>
    public const string TitleEmoji = "🎉";

    public const string LearnedHeading = "O que você aprendeu";

    public static string Footer(int modulePoints, string pathTitle, int pathPercentage) =>
        $"+{modulePoints} pontos neste módulo · Trilha {pathTitle}: {Math.Clamp(pathPercentage, 0, 100)}%";

    /// <summary>Botão principal: 1ª aula do próximo módulo que tem aula pronta.</summary>
    public static string StartNextModule(int number, string title) => $"Começar o módulo {number}: {title}";

    /// <summary>Mesmo destino, quando o aluno já começou o próximo módulo (ex.: revendo o resumo depois).</summary>
    public static string GoToNextModule(int number, string title) => $"Ir para o módulo {number}: {title}";

    /// <summary>Depois do último módulo com aula pronta (texto do #42). Sem botão para rascunhos.</summary>
    public const string EverythingDone =
        "Você terminou tudo o que está pronto até agora. Novas aulas estão chegando, e seu progresso fica guardado.";

    /// <summary>Selo do módulo concluído no dashboard (US-M.1); o link abre o resumo de novo.</summary>
    public const string DashboardBadge = "Concluído ✓";
    public static string DashboardBadgeSrOnly(string moduleTitle) => $"Módulo concluído. Ver o resumo do módulo {moduleTitle}";

    /// <summary>Linha do dashboard quando o "Continuar" leva ao começo de um módulo novo.</summary>
    public static string DashboardNextModule(int number, string title) => $"Próximo passo: módulo {number}, {title}";
}
