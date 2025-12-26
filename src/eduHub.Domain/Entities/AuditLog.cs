using System;
using eduHub.Domain.Interfaces;

namespace eduHub.Domain.Entities;

public class AuditLog : ITenantScoped
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}
