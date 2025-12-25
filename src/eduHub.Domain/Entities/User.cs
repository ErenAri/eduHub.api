using eduHub.Domain.Enums;

namespace eduHub.Domain.Entities;

public class User
{
    public int Id { get; set; }

    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public string PasswordHash { get; set; } = null!;
    public UserRole Role { get; set; } = UserRole.User;
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<RevokedToken> RevokedTokens { get; set; } = new List<RevokedToken>();
}
