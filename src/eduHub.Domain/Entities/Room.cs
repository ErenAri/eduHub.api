using System;
using System.Collections.Generic;
using eduHub.Domain.Interfaces;

namespace eduHub.Domain.Entities;

public class Room : ITenantScoped
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public bool IsDeleted { get; set; }
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public int BuildingId { get; set; }
    public Building Building { get; set; } = null!;
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
