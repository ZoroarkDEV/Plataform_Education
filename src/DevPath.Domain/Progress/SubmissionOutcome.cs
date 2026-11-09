namespace DevPath.Domain.Progress;

/// <summary>
/// O que aconteceu numa execução gravada (#43): certo, errado ou erro. Mais fino que <see cref="SubmissionResult"/>,
/// que só diz se passou (é o que as regras de tentativa usam). Gravado em <see cref="CodeSubmission.Outcome"/>.
/// </summary>
public enum SubmissionOutcome
{
    /// <summary>Resultado esperado (certo).</summary>
    Pass = 0,

    /// <summary>Rodou, mas o resultado é diferente do esperado (errado).</summary>
    Fail = 1,

    /// <summary>Erro do código do aluno: sintaxe, execução, memória ou saída grande demais.</summary>
    Error = 2,

    /// <summary>Demorou demais e foi interrompido.</summary>
    Timeout = 3
}
