using System.Collections.Generic;
using eduHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace eduHub.api.Authorization;

public sealed class OrgRoleRequirement : IAuthorizationRequirement
{
    public OrgRoleRequirement(params OrganizationMemberRole[] allowedRoles)
    {
        AllowedRoles = allowedRoles ?? new OrganizationMemberRole[0];
    }

    public IReadOnlyCollection<OrganizationMemberRole> AllowedRoles { get; }
}
