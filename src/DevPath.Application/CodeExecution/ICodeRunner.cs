namespace DevPath.Application.CodeExecution;

/// <summary>
/// Executa código de UMA linguagem usando o <see cref="ICodeSandbox"/>.
/// Ex.: o runner de SQL sabe subir um PostgreSQL descartável e rodar o script do aluno.
/// </summary>
public interface ICodeRunner
{
    /// <summary>Linguagem atendida, igual a <c>Exercise.Language</c> (minúsculas, ex.: "sql").</summary>
    string Language { get; }

    /// <summary>Roda o código do aluno, opcionalmente depois de um script de preparação.</summary>
    /// <param name="code">Código do aluno.</param>
    /// <param name="setup">Preparação do caso de teste (ex.: CREATE TABLE + INSERT). Roda antes do código.</param>
    Task<SandboxResult> RunAsync(string code, string? setup = null, CancellationToken cancellationToken = default);
}
