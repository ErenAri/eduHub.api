using eduHub.Application.DTOs.Users;

namespace eduHub.Application.Interfaces.Users;

public interface IUserService
{
    Task<UserResponseDto> RegisterAsync(UserRegisterDto dto);
    Task<AuthResponseDto?> LoginAsync(UserLoginDto dto);
    Task<AuthResponseDto?> RefreshAsync(RefreshRequestDto dto);
    Task RevokeTokenAsync(string jti, DateTimeOffset expiresAtUtc, int userId);
    Task RevokeRefreshTokensAsync(int userId);
}
