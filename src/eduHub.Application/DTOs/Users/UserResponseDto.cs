namespace eduHub.Application.DTOs.Users;

public class UserResponseDto
{
    public int Id { get; set; }
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = null!;
    public bool IsPlatformAdmin { get; set; }
}
