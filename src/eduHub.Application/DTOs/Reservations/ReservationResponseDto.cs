namespace eduHub.Application.DTOs.Reservations
{
    public class ReservationResponseDto
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public DateTimeOffset Start { get; set; }

        public DateTimeOffset End { get; set; }

        public string Purpose { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public int? CreatedByUserId { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}
