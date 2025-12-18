namespace eduHub.Application.DTOs.Users;

public class AuthResponseDto
{
    public string AccessToken { get; set; } = null!;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string RefreshToken { get; set; } = null!;
    public DateTimeOffset RefreshTokenExpiresAtUtc { get; set; }

    public UserResponseDto User { get; set; } = null!;
}
