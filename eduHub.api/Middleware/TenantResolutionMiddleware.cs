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
    private static readonly string[] ReservedSubdomains = { "www" };

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
            var slug = ResolveTenantSlug(context);
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

    private static string? ResolveTenantSlug(HttpContext context)
    {
        var headerSlug = context.Request.Headers["x-tenant-slug"].FirstOrDefault();
        var normalizedHeader = NormalizeSlug(headerSlug);
        if (!string.IsNullOrWhiteSpace(normalizedHeader))
            return normalizedHeader;

        var forwardedHost = context.Request.Headers["x-forwarded-host"].FirstOrDefault();
        var forwardedSlug = ExtractSubdomain(forwardedHost);
        if (!string.IsNullOrWhiteSpace(forwardedSlug))
            return forwardedSlug;

        return ExtractSubdomain(context.Request.Host.Host);
    }

    private static string? ExtractSubdomain(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var hostname = host.Split(':')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(hostname))
            return null;

        if (hostname is "localhost" or "127.0.0.1" or "::1")
            return null;

        if (hostname.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = hostname[..^".localhost".Length];
            var subdomain = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return NormalizeSlug(subdomain);
        }

        var parts = hostname.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 2)
            return null;

        return NormalizeSlug(parts[0]);
    }

    private static string? NormalizeSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var trimmed = slug.Trim().ToLowerInvariant();
        if (ReservedSubdomains.Contains(trimmed))
            return null;

        return trimmed;
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
