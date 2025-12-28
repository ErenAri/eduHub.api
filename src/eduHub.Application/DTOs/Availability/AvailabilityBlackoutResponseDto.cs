using System;

namespace eduHub.Application.DTOs.Availability;

public class AvailabilityBlackoutResponseDto
{
    public int Id { get; set; }
    public DateTimeOffset StartTimeUtc { get; set; }
    public DateTimeOffset EndTimeUtc { get; set; }
    public string? Reason { get; set; }
    public int? BuildingId { get; set; }
    public int? RoomId { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
