namespace eduHub.Application.DTOs.Reservations;

public class ReservationPolicyDto
{
    public int LeadTimeMinutes { get; set; }
    public int MaxAdvanceDays { get; set; }
    public int SlotMinutes { get; set; }
    public int MaxDurationMinutes { get; set; }
    public int BufferMinutes { get; set; }
    public int PendingExpiryHours { get; set; }
    public int MaxPendingPerUser { get; set; }
    public int GuestAccessHours { get; set; }
}
