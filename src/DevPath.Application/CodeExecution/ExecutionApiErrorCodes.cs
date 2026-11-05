namespace DevPath.Application.CodeExecution;

/// <summary>
/// Códigos estáveis nos corpos 400 de <c>POST /api/exercicios/{id}/executar</c> (#27, ADR-007).
/// A UI compara o <c>code</c>, não o texto de <c>message</c>.
/// </summary>
public static class ExecutionApiErrorCodes
{
    public const string CodeEmpty = "code_empty";
    public const string CodeTooLong = "code_too_long";
    public const string AntiforgeryInvalid = "antiforgery_invalid";
    public const string MalformedBody = "malformed_body";
}
