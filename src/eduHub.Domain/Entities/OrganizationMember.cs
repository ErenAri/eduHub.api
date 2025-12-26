using System;
using eduHub.Domain.Enums;
using eduHub.Domain.Interfaces;

namespace eduHub.Domain.Entities;

public class OrganizationMember : ITenantScoped
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public OrganizationMemberRole Role { get; set; }
    public OrganizationMemberStatus Status { get; set; }
    public DateTimeOffset JoinedAtUtc { get; set; }
}
