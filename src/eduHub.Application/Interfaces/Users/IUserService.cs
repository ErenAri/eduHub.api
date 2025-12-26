using eduHub.Application.DTOs.Organizations;
using eduHub.Application.DTOs.Users;

namespace eduHub.Application.Interfaces.Users;

public interface IUserService
{
    Task<UserResponseDto> RegisterAsync(UserRegisterDto dto);
    Task<AuthResponseDto?> LoginAsync(UserLoginDto dto, Guid organizationId);
    Task<AuthResponseDto?> RefreshAsync(RefreshRequestDto dto, Guid organizationId);
    Task<AuthResponseDto> RedeemInviteAsync(OrganizationInviteRedeemDto dto, Guid organizationId);
    Task<UserResponseDto?> GetByIdAsync(int userId);
    Task<UserResponseDto?> GetByIdInOrgAsync(int userId, Guid organizationId);
    Task<UserResponseDto> UpdateProfileAsync(int userId, UserProfileUpdateDto dto);
    Task ChangePasswordAsync(int userId, UserChangePasswordDto dto);
    Task<string> UpdateAvatarAsync(int userId, string avatarUrl);
    Task RevokeTokenAsync(string jti, DateTimeOffset expiresAtUtc, int userId);
    Task RevokeRefreshTokensAsync(int userId);
}
