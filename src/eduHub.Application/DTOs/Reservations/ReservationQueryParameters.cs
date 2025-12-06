namespace eduHub.Application.DTOs.Reservations;

public class ReservationQueryParameters
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    public int? BuildingId { get; set; }
    public int? RoomId { get; set; }
    public DateTime? StartTimeUtc { get; set; }
    public DateTime? EndTimeUtc { get; set; }

    /// <summary>
    /// Optional sort selector. Supported: "start" (default) or "start_desc".
    /// </summary>
    public string? Sort { get; set; }
}
