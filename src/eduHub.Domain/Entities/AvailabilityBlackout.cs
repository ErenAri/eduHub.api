using System;
using eduHub.Domain.Interfaces;

namespace eduHub.Domain.Entities;

public class AvailabilityBlackout : ITenantScoped
{
    public int Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public int? BuildingId { get; set; }
    public Building? Building { get; set; }
    public int? RoomId { get; set; }
    public Room? Room { get; set; }
    public DateTimeOffset StartTimeUtc { get; set; }
    public DateTimeOffset EndTimeUtc { get; set; }
    public string? Reason { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
