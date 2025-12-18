using System;
using System.Collections.Generic;
using System.Text;

namespace eduHub.Application.DTOs.Reservations
{
    public class ReservationCreateDto
    {
        public int RoomId { get; set; }
        public DateTimeOffset StartTimeUtc { get; set; }
        public DateTimeOffset EndTimeUtc { get; set; }
        public string Purpose { get; set; } = null!;
    }
}
