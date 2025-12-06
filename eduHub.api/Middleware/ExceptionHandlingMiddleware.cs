using System.Net;
using System.Text.Json;
using eduHub.api.Models;

namespace eduHub.api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        var error = new ErrorResponse();
        HttpStatusCode statusCode;

        switch (exception)
        {
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

            default:
                statusCode = HttpStatusCode.InternalServerError;
                error.Code = "ServerError";
                error.Message = exception.Message;
                error.Details = exception.StackTrace; 
                break;
        }

        response.StatusCode = (int)statusCode;

        var json = JsonSerializer.Serialize(error);
        await response.WriteAsync(json);
    }
}
