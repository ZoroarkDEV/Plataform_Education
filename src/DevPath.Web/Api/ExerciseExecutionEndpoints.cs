using System.Security.Claims;
using DevPath.Application.CodeExecution;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace DevPath.Web.Api;

/// <summary>Corpo de <c>POST /api/exercicios/{exerciseId}/executar</c>.</summary>
public sealed record ExecuteExerciseRequest(string? Code);

/// <summary>Erro da API: sempre <c>{ "message": "..." }</c> em português.</summary>
public sealed record ApiError(string Message);

/// <summary>
/// API de execução de exercícios (issue #25). O contrato para o Frontend está no ADR-006.
/// </summary>
/// <remarks>
/// A Web NÃO executa código: confere login, CSRF, limite e se a aula está liberada, e então põe o job
/// na fila do Redis (<see cref="IExecutionQueue"/>). Quem executa é o DevPath.Worker.
/// </remarks>
public static class ExerciseExecutionEndpoints
{
    /// <summary>Header onde o JavaScript manda o token antiforgery (o padrão do ASP.NET Core).</summary>
    public const string AntiforgeryHeaderName = "RequestVerificationToken";

    public const string EmptyCodeMessage = "Escreva uma consulta SQL antes de executar.";
    public const string NotFoundMessage = "Exercício não encontrado.";
    public const string UnavailableMessage = "O executor de código está indisponível no momento. Tente de novo em instantes.";
    public const string AntiforgeryMessage = "Sua sessão expirou ou a página está desatualizada. Recarregue a página e tente de novo.";

    public static IServiceCollection AddExerciseExecutionApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAntiforgery(options => options.HeaderName = AntiforgeryHeaderName);
        services.AddExecutionRateLimiting(configuration);

        // Em /api, quem não está logado recebe 401 (e não um redirect para a tela de login, que o fetch
        // seguiria sem o aluno ver). As páginas normais continuam redirecionando.
        services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
        {
            var toLogin = options.Events.OnRedirectToLogin;
            var toDenied = options.Events.OnRedirectToAccessDenied;
            options.Events.OnRedirectToLogin = context => IsApi(context.Request)
                ? WriteStatus(context.Response, StatusCodes.Status401Unauthorized)
                : toLogin(context);
            options.Events.OnRedirectToAccessDenied = context => IsApi(context.Request)
                ? WriteStatus(context.Response, StatusCodes.Status403Forbidden)
                : toDenied(context);
        });

        return services;
    }

    public static IEndpointRouteBuilder MapExerciseExecutionApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // O token antiforgery é ligado ao usuário logado: o Frontend busca DEPOIS do login (ou lê do
        // <input name="__RequestVerificationToken"> que o Razor já coloca nos formulários).
        api.MapGet("/antiforgery/token", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken, headerName = AntiforgeryHeaderName });
        });

        api.MapPost("/exercicios/{exerciseId:guid}/executar", ExecuteAsync)
            .WithMetadata(new ExecutionRateLimitedAttribute())
            .AddEndpointFilter<ValidateAntiforgeryFilter>();

        return app;
    }

    private static async Task<IResult> ExecuteAsync(
        Guid exerciseId,
        ExecuteExerciseRequest? body,
        ClaimsPrincipal user,
        IExerciseCatalog catalog,
        ILessonAccessPolicy accessPolicy,
        IExecutionQueue queue,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Results.Unauthorized();
        }

        var code = body?.Code;
        if (string.IsNullOrWhiteSpace(code))
        {
            return Results.BadRequest(new ApiError(EmptyCodeMessage));
        }
        if (code.Length > ExecutionLimits.MaxCodeLength)
        {
            return Results.BadRequest(new ApiError($"O código pode ter no máximo {ExecutionLimits.MaxCodeLength} caracteres."));
        }

        // Inexistente, escondido ou rascunho: o mesmo 404 e a mesma mensagem (não revela qual é o caso).
        var visibility = await catalog.GetVisibilityAsync(exerciseId, cancellationToken);
        if (accessPolicy.Evaluate(userId, visibility) != ExerciseAccess.Allowed)
        {
            return Results.NotFound(new ApiError(NotFoundMessage));
        }

        try
        {
            // 200 para pass/fail/error/timeout/attempts_exhausted: o pedido foi processado, e o status está no corpo.
            return Results.Ok(await queue.ExecuteAsync(userId, exerciseId, code, cancellationToken));
        }
        catch (ExecutionUnavailableException)
        {
            return Results.Json(new ApiError(UnavailableMessage), statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool IsApi(HttpRequest request) => request.Path.StartsWithSegments("/api");

    private static Task WriteStatus(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Confere o token antiforgery (cookie + header <see cref="AntiforgeryHeaderName"/>). Sem isso, outro site
    /// poderia fazer o navegador do aluno, já logado, gastar as execuções e tentativas dele (CSRF).
    /// </summary>
    private sealed class ValidateAntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new ApiError(AntiforgeryMessage));
            }
            return await next(context);
        }
    }
}
