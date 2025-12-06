namespace eduHub.Application.DTOs.Reservations
{
    public class ReservationResponseDto
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public string Purpose { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}
