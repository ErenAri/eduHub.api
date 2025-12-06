using System;
using System.Collections.Generic;
using System.Text;

namespace eduHub.Application.DTOs.Buildings
{
    public class BuildingUpdateDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }
}
