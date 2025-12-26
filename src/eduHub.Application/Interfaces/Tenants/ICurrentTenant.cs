using System;

namespace eduHub.Application.Interfaces.Tenants;

public interface ICurrentTenant
{
    Guid? OrganizationId { get; }
    bool IsPlatformScope { get; }
}
