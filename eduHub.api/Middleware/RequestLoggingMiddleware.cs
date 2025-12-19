using System.Diagnostics;
using eduHub.api.Options;
using Microsoft.Extensions.Options;

namespace eduHub.api.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly RequestLoggingOptions _options;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger,
        IOptions<RequestLoggingOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        await _next(context);
        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds >= _options.SlowRequestThresholdMs)
        {
            _logger.LogWarning(
                "Slow request {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }
}
