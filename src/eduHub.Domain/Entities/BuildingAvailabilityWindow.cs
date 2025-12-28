using System;
using eduHub.Domain.Interfaces;

namespace eduHub.Domain.Entities;

public class BuildingAvailabilityWindow : ITenantScoped
{
    public int Id { get; set; }
    public int BuildingId { get; set; }
    public Building Building { get; set; } = null!;
    public DayOfWeek DayOfWeek { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
}
