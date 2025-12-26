using System;
using System.Collections.Generic;
using eduHub.Domain.Interfaces;

namespace eduHub.Domain.Entities;

public class Building : ITenantScoped
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public bool IsDeleted { get; set; }
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
}
