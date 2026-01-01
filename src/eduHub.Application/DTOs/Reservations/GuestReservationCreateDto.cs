using System;

namespace eduHub.Application.DTOs.Reservations;

public class GuestReservationCreateDto
{
    public int RoomId { get; set; }
    public DateTimeOffset StartTimeUtc { get; set; }
    public DateTimeOffset EndTimeUtc { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string GuestToken { get; set; } = string.Empty;
}
