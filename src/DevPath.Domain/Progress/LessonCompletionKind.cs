namespace DevPath.Domain.Progress;

/// <summary>Como o aluno concluiu uma aula (#27, ADR-007). Gravado em <see cref="UserProgress.CompletionKind"/>.</summary>
public enum LessonCompletionKind
{
    /// <summary>Aula só de leitura, concluída com o clique. Não vale pontos.</summary>
    Read = 0,

    /// <summary>Todos os exercícios da aula passaram (submissão <c>pass</c>). Vale os pontos dos exercícios.</summary>
    ExercisePassed = 1,

    /// <summary>
    /// O exercício tem limite (<c>MaxAttempts</c>) e o aluno usou todas as tentativas sem passar. 0 ponto.
    /// Não dá mais para tentar.
    /// </summary>
    AttemptsExhausted = 2,

    /// <summary>
    /// "Seguir sem pontos": o aluno errou o exercício o bastante (<c>FailuresBeforeSkip</c>, padrão 5) e escolheu seguir.
    /// 0 ponto, mas ele continua podendo tentar: um <c>pass</c> depois promove para <see cref="ExercisePassed"/>.
    /// </summary>
    SkippedAfterFailures = 3
}
