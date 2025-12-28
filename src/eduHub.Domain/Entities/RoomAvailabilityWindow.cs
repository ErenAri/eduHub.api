using System;
using eduHub.Domain.Interfaces;

namespace eduHub.Domain.Entities;

public class RoomAvailabilityWindow : ITenantScoped
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public DayOfWeek DayOfWeek { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
}
