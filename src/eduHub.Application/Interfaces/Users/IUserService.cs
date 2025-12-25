using eduHub.Application.DTOs.Users;

namespace eduHub.Application.Interfaces.Users;

public interface IUserService
{
    Task<UserResponseDto> RegisterAsync(UserRegisterDto dto);
    Task<AuthResponseDto?> LoginAsync(UserLoginDto dto);
    Task<AuthResponseDto?> RefreshAsync(RefreshRequestDto dto);
    Task<UserResponseDto?> GetByIdAsync(int userId);
    Task<UserResponseDto> UpdateProfileAsync(int userId, UserProfileUpdateDto dto);
    Task ChangePasswordAsync(int userId, UserChangePasswordDto dto);
    Task<string> UpdateAvatarAsync(int userId, string avatarUrl);
    Task RevokeTokenAsync(string jti, DateTimeOffset expiresAtUtc, int userId);
    Task RevokeRefreshTokensAsync(int userId);
}
