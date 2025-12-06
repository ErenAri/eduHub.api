using System;
using System.Collections.Generic;
using System.Text;

namespace eduHub.Application.DTOs.Rooms
{
    public class RoomResponseDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public int Capacity { get; set; }
        public int BuildingId { get; set; }
    }
}
