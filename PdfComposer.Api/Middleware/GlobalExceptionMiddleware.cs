using System.Net;
using System.Text.Json;

namespace PdfComposer.Api.Middleware;

public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while processing {Path}", context.Request.Path);

            context.Response.StatusCode = (int)MapStatusCode(ex);
            context.Response.ContentType = "application/json";

            var payload = JsonSerializer.Serialize(new
            {
                error = ex.Message,
                traceId = context.TraceIdentifier
            });

            await context.Response.WriteAsync(payload);
        }
    }

    private static HttpStatusCode MapStatusCode(Exception exception)
    {
        return exception switch
        {
            NotSupportedException => HttpStatusCode.BadRequest,
            FileNotFoundException => HttpStatusCode.BadRequest,
            InvalidOperationException => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.InternalServerError
        };
    }
}
