using System;

namespace eduHub.Domain.Interfaces;

public interface ITenantScoped
{
    Guid OrganizationId { get; set; }
}
