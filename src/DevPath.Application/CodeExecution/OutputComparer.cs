namespace DevPath.Application.CodeExecution;

/// <summary>
/// Compara a saída do aluno com a esperada, ignorando diferenças que não importam
/// para quem está aprendendo: \r\n vs \n, espaços no fim da linha e linhas vazias nas pontas.
/// </summary>
public static class OutputComparer
{
    public static bool AreEquivalent(string expected, string actual) => Normalize(expected) == Normalize(actual);

    public static string Normalize(string value)
    {
        var lines = value.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd());
        return string.Join('\n', lines).Trim('\n');
    }
}
