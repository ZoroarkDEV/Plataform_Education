namespace DevPath.Domain.CareerPaths;

/// <summary>
/// Um erro comum de iniciante num exercício e a mensagem amigável para ele
/// (ex.: aspas duplas em texto -> "Textos em SQL vão entre aspas simples").
/// Gravado em <see cref="Exercise.CommonErrors"/> (jsonb: <c>[{"example": "...", "message": "..."}]</c>).
/// </summary>
/// <param name="Example">Consulta errada típica. Nunca pode passar nos casos de teste.</param>
/// <param name="Message">O que mostrar ao aluno quando o erro dele for esse.</param>
public sealed record CommonError(string Example, string Message);
