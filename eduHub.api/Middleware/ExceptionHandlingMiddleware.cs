using System.Net;
using System.Text.Json;
using eduHub.api.Models;
using Microsoft.Extensions.Hosting;
using eduHub.Application.Common.Exceptions;


namespace eduHub.api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception caught by middleware");
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        var error = new ErrorResponse();
        HttpStatusCode statusCode;

        switch (exception)
        {
            case ConflictException:
                statusCode = HttpStatusCode.Conflict;
                error.Code = "Conflict";
                error.Message = exception.Message;
                error.Details = new { traceId = context.TraceIdentifier };
                break;

            case InvalidOperationException:
                statusCode = HttpStatusCode.BadRequest;
                error.Code = "InvalidOperation";
                error.Message = exception.Message;
                error.Details = null;
                break;

            case UnauthorizedAccessException:
                statusCode = HttpStatusCode.Unauthorized;
                error.Code = "Unauthorized";
                error.Message = exception.Message;
                error.Details = null;
                break;

            case KeyNotFoundException:
                statusCode = HttpStatusCode.NotFound;
                error.Code = "NotFound";
                error.Message = exception.Message;
                error.Details = null;
                break;

            default:
                statusCode = HttpStatusCode.InternalServerError;
                error.Code = "ServerError";
                error.Message = "An unexpected error occurred.";
                error.Details = _environment.IsDevelopment()
                    ? new
                    {
                        traceId = context.TraceIdentifier,
                        error = exception.Message,
                        stackTrace = exception.StackTrace
                    }
                    : new
                    {
                        traceId = context.TraceIdentifier
                    };
                break;
        }

        response.StatusCode = (int)statusCode;

        var json = JsonSerializer.Serialize(error);
        await response.WriteAsync(json);
    }
}
