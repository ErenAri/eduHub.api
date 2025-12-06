using eduHub.Domain.Enums;

namespace eduHub.Domain.Entities;

public class User
{
    public int Id { get; set; }

    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
