using System;

namespace eduHub.Application.DTOs.Availability;

public class AvailabilitySlotDto
{
    public DateTimeOffset StartTimeUtc { get; set; }
    public DateTimeOffset EndTimeUtc { get; set; }
}
