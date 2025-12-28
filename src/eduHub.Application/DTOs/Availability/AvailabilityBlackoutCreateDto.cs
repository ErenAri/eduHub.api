using System;

namespace eduHub.Application.DTOs.Availability;

public class AvailabilityBlackoutCreateDto
{
    public DateTimeOffset StartTimeUtc { get; set; }
    public DateTimeOffset EndTimeUtc { get; set; }
    public string? Reason { get; set; }
    public int? BuildingId { get; set; }
    public int? RoomId { get; set; }
}
