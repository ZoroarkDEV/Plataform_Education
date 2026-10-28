using System.Text.Json;

namespace DevPath.Infrastructure.CodeExecution;

/// <summary>Formato JSON das mensagens no Redis (o mesmo dos dois lados da fila).</summary>
internal static class ExecutionJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
