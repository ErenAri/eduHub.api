using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using eduHub.Application.Interfaces.Tenants;
using eduHub.Application.Security;
using eduHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace eduHub.api.Authorization;

public sealed class OrgRoleHandler : AuthorizationHandler<OrgRoleRequirement>
{
    private readonly ICurrentTenant _tenant;

    public OrgRoleHandler(ICurrentTenant tenant)
    {
        _tenant = tenant;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrgRoleRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var platformClaim = context.User.FindFirst(TenantClaimTypes.IsPlatformAdmin)?.Value;
        if (string.Equals(platformClaim, "true", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (_tenant.IsPlatformScope || !_tenant.OrganizationId.HasValue)
            return Task.CompletedTask;

        var orgIdClaim = context.User.FindFirst(TenantClaimTypes.OrganizationId)?.Value;
        if (!Guid.TryParse(orgIdClaim, out var orgId))
            return Task.CompletedTask;

        if (orgId != _tenant.OrganizationId.Value)
            return Task.CompletedTask;

        var roleClaim = context.User.FindFirst(TenantClaimTypes.OrganizationRole)?.Value;
        if (string.IsNullOrWhiteSpace(roleClaim))
            return Task.CompletedTask;

        if (!Enum.TryParse<OrganizationMemberRole>(roleClaim, ignoreCase: true, out var role))
            return Task.CompletedTask;

        if (requirement.AllowedRoles.Count == 0 ||
            requirement.AllowedRoles.Contains(role))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
