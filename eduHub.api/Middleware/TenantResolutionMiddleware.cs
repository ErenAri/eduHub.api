using System;
using System.Linq;
using System.Threading.Tasks;
using eduHub.Application.Interfaces.Tenants;
using eduHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace eduHub.api.Middleware;

public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ProblemDetailsFactory _problemDetailsFactory;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ProblemDetailsFactory problemDetailsFactory)
    {
        _next = next;
        _problemDetailsFactory = problemDetailsFactory;
    }

    public async Task InvokeAsync(
        HttpContext context,
        AppDbContext db,
        ICurrentTenantSetter tenantSetter)
    {
        var path = context.Request.Path;

        if (path.StartsWithSegments("/api/platform", StringComparison.OrdinalIgnoreCase))
        {
            tenantSetter.SetPlatformScope();
            await _next(context);
            return;
        }

        if (path.StartsWithSegments("/api/org", StringComparison.OrdinalIgnoreCase))
        {
            var host = context.Request.Host.Host;
            var slug = ExtractSubdomain(host);
            if (string.IsNullOrWhiteSpace(slug))
            {
                await WriteTenantNotFoundAsync(context);
                return;
            }

            var org = await db.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Slug == slug);

            if (org == null || !org.IsActive)
            {
                await WriteTenantNotFoundAsync(context);
                return;
            }

            tenantSetter.SetTenant(org.Id);
            await _next(context);
            return;
        }

        await _next(context);
    }

    private static string? ExtractSubdomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return null;

        return parts[0];
    }

    private async Task WriteTenantNotFoundAsync(HttpContext context)
    {
        var problem = _problemDetailsFactory.CreateProblemDetails(
            context,
            statusCode: StatusCodes.Status404NotFound,
            title: "Tenant not found",
            detail: "Tenant not found.",
            type: "https://httpstatuses.com/404");
        problem.Extensions["code"] = "TenantNotFound";
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, cancellationToken: context.RequestAborted);
    }
}
