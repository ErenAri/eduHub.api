using System;

namespace eduHub.Application.Interfaces.Tenants;

public interface ICurrentTenantSetter
{
    void SetTenant(Guid organizationId);
    void SetPlatformScope();
    void Clear();
}
