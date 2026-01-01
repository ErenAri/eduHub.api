namespace eduHub.Application.Options;

public class ReservationPolicyOptions
{
    public const string SectionName = "Reservations:Policy";

    public int LeadTimeMinutes { get; set; } = 15;
    public int MaxAdvanceDays { get; set; } = 30;
    public int SlotMinutes { get; set; } = 60;
    public int MaxDurationMinutes { get; set; } = 120;
    public int BufferMinutes { get; set; } = 15;
    public int PendingExpiryHours { get; set; } = 24;
    public int MaxPendingPerUser { get; set; } = 3;
    public int GuestAccessHours { get; set; } = 24;
}
