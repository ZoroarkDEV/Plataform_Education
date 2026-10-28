namespace DevPath.Application.CodeExecution;

/// <summary>
/// Executa código de UMA linguagem usando o <see cref="ICodeSandbox"/>.
/// Ex.: o runner de SQL sabe subir um PostgreSQL descartável e rodar a consulta do aluno.
/// </summary>
public interface ICodeRunner
{
    /// <summary>Linguagem atendida, igual a <c>Exercise.Language</c> (minúsculas, ex.: "sql").</summary>
    string Language { get; }

    /// <summary>Roda o código do aluno depois de uma preparação CONFIÁVEL.</summary>
    /// <param name="code">Código do aluno (não confiável).</param>
    /// <param name="preparation">
    /// Script da equipe (dataset + setup do caso de teste). Roda antes, com privilégios que o aluno não tem.
    /// Nunca coloque aqui o resultado esperado nem a solução: tudo que entra no container pode vazar.
    /// </param>
    Task<SandboxResult> RunAsync(string code, string? preparation = null, CancellationToken cancellationToken = default);
}
