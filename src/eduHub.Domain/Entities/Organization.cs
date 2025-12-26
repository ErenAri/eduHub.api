using System;
using System.Collections.Generic;

namespace eduHub.Domain.Entities;

public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? Timezone { get; set; }
    public string? SubscriptionPlan { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int? CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }
    public ICollection<OrganizationMember> Members { get; set; } = new List<OrganizationMember>();
    public ICollection<OrganizationInvite> Invites { get; set; } = new List<OrganizationInvite>();
}
