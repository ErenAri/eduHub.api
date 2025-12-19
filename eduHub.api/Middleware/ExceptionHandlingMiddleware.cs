using System.Net;
using System.Security.Claims;
using eduHub.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Hosting;


namespace eduHub.api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;
    private readonly ProblemDetailsFactory _problemDetailsFactory;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment,
        ProblemDetailsFactory problemDetailsFactory)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
        _problemDetailsFactory = problemDetailsFactory;
    }

    public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
                var path = context.Request.Path.Value ?? string.Empty;
                _logger.LogError(ex, "Unhandled exception {TraceId} for user {UserId} at {Path}", context.TraceIdentifier, userId, path);
                await HandleExceptionAsync(context, ex);
            }
        }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        HttpStatusCode statusCode;
        string title;
        string detail;
        string code;
        object? extraDetails = null;

        switch (exception)
        {
            case ConflictException:
                statusCode = HttpStatusCode.Conflict;
                code = "Conflict";
                title = "Conflict";
                detail = _environment.IsDevelopment()
                    ? exception.Message
                    : "Request could not be completed.";
                break;

            case InvalidOperationException:
                statusCode = HttpStatusCode.BadRequest;
                code = "InvalidOperation";
                title = "Invalid request";
                detail = _environment.IsDevelopment()
                    ? exception.Message
                    : "Invalid request.";
                break;

            case UnauthorizedAccessException:
                if (context.User.Identity?.IsAuthenticated == true)
                {
                    statusCode = HttpStatusCode.Forbidden;
                    code = "Forbidden";
                    title = "Forbidden";
                    detail = "Forbidden.";
                }
                else
                {
                    statusCode = HttpStatusCode.Unauthorized;
                    code = "Unauthorized";
                    title = "Unauthorized";
                    detail = "Unauthorized.";
                }
                break;

            case KeyNotFoundException:
                statusCode = HttpStatusCode.NotFound;
                code = "NotFound";
                title = "Not found";
                detail = "Not found.";
                break;

            default:
                statusCode = HttpStatusCode.InternalServerError;
                code = "ServerError";
                title = "Server error";
                detail = "An unexpected error occurred.";
                if (_environment.IsDevelopment())
                {
                    extraDetails = new
                    {
                        exception = exception.GetType().FullName,
                        message = exception.Message,
                        stackTrace = exception.StackTrace
                    };
                }
                break;
        }

        var status = (int)statusCode;
        var problem = _problemDetailsFactory.CreateProblemDetails(
            context,
            statusCode: status,
            title: title,
            detail: detail,
            type: $"https://httpstatuses.com/{status}");

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (extraDetails != null)
            problem.Extensions["details"] = extraDetails;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, cancellationToken: context.RequestAborted);
    }
}
