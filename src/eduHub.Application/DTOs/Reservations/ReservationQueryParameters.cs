namespace eduHub.Application.DTOs.Reservations;

public class ReservationQueryParameters
{
    public int PageSize { get; set; } = 10;
    public string? Cursor { get; set; }

    public int? BuildingId { get; set; }
    public int? RoomId { get; set; }
    public DateTimeOffset? StartTimeUtc { get; set; }
    public DateTimeOffset? EndTimeUtc { get; set; }

    /// <summary>
    /// Optional sort selector. Supported: "start" (default) or "start_desc".
    /// </summary>
    public string? Sort { get; set; }
}
