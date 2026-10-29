using DevPath.Infrastructure;

// Worker de execução de código (issue #25, ADR-006).
// Configuração por variáveis de ambiente:
//   ConnectionStrings__DevPath  PostgreSQL da plataforma (grava as CodeSubmissions)
//   ConnectionStrings__Redis    Redis da fila (ex.: redis:6379)
//   Sandbox__*                  limites do container (veja SandboxOptions)
//   Execution__*                fila e tempos (veja ExecutionQueueOptions)
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSandboxExecution(builder.Configuration);

builder.Build().Run();
