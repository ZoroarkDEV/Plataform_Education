namespace DevPath.Domain.Progress;

/// <summary>Em que ponto o aluno está em uma aula, exercício ou módulo.</summary>
public enum ProgressStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
    Mastered = 3
}
