using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InstaTalk.API.Middlewares;

public class GlobalExceptionHandler: IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // 1. Logamos o erro real internamente de forma segura (para o time de Dev/SecOps)
        _logger.LogError(exception, "Ocorreu uma exceção não tratada. RequestId: {RequestId}", httpContext.TraceIdentifier);

        // 2. Montamos uma resposta estéril e genérica para o atacante/usuário
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Detail = "An unexpected error occurred. Our engineers have been notified.",
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = problemDetails.Status.Value;

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        // Retorna true para sinalizar que a exceção foi capturada e tratada
        return true;
    }
}
