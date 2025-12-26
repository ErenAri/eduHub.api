using System;
using eduHub.Application.Interfaces.Tenants;

namespace eduHub.Infrastructure.Tenancy;

public sealed class CurrentTenant : ICurrentTenant, ICurrentTenantSetter
{
    public Guid? OrganizationId { get; private set; }
    public bool IsPlatformScope { get; private set; }

    public void SetTenant(Guid organizationId)
    {
        OrganizationId = organizationId;
        IsPlatformScope = false;
    }

    public void SetPlatformScope()
    {
        OrganizationId = null;
        IsPlatformScope = true;
    }

    public void Clear()
    {
        OrganizationId = null;
        IsPlatformScope = false;
    }
}
