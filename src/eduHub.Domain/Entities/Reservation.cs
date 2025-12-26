using System;
using eduHub.Domain.Enums;
using eduHub.Domain.Interfaces;

namespace eduHub.Domain.Entities;

public class Reservation : ITenantScoped
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public DateTimeOffset StartTimeUtc { get; set; }
    public DateTimeOffset EndTimeUtc { get; set; }
    public string Purpose { get; set; } = null!;
    public ReservationStatus Status { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
}
