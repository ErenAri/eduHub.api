namespace eduHub.Application.DTOs.Users;

public class AuthResponseDto
{
    public string AccessToken { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }

    public UserResponseDto User { get; set; } = null!;
}
