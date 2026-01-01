using System;
using eduHub.Domain.Interfaces;

namespace eduHub.Domain.Entities;

public class GuestReservationToken : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string Email { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public string? CreatedFromIp { get; set; }
}
