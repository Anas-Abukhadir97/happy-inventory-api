using Microsoft.AspNetCore.Diagnostics;

namespace HappyInventory.Api;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            return false;

        // Provider exceptions can contain request data. Record only the type and trace ID.
        logger.LogError("Unhandled request failure. TraceId: {TraceId}; ExceptionType: {ExceptionType}",
            context.TraceIdentifier, exception.GetType().FullName);
        await ApiProblemResponses.WriteAsync(context, 500, "internal_error", "UnexpectedError");
        return true;
    }
}
