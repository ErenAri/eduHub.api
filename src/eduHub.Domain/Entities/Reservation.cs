using System;
using eduHub.Domain.Enums;

namespace eduHub.Domain.Entities;

public class Reservation
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
    public string Purpose { get; set; } = null!;
    public ReservationStatus Status { get; set; }

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
