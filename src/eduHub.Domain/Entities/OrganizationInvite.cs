using System;
using eduHub.Domain.Enums;
using eduHub.Domain.Interfaces;

namespace eduHub.Domain.Entities;

public class OrganizationInvite : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public string Email { get; set; } = string.Empty;
    public OrganizationMemberRole Role { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public int? UsedByUserId { get; set; }
    public User? UsedByUser { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
