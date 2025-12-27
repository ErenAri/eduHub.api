using eduHub.Application.DTOs.Organizations;
using eduHub.Application.DTOs.Users;

namespace eduHub.Application.Interfaces.Users;

public interface IUserService
{
    Task<UserResponseDto> RegisterAsync(UserRegisterDto dto);
    Task<AuthResponseDto> RegisterInOrganizationAsync(UserRegisterDto dto, Guid organizationId);
    Task<AuthResponseDto?> LoginAsync(UserLoginDto dto, Guid organizationId);
    Task<AuthResponseDto?> LoginPlatformAsync(UserLoginDto dto);
    Task<AuthResponseDto?> RefreshAsync(RefreshRequestDto dto, Guid organizationId);
    Task<AuthResponseDto?> RefreshPlatformAsync(RefreshRequestDto dto);
    Task<AuthResponseDto> RedeemInviteAsync(OrganizationInviteRedeemDto dto, Guid organizationId);
    Task<UserResponseDto?> GetByIdAsync(int userId);
    Task<UserResponseDto?> GetByIdInOrgAsync(int userId, Guid organizationId);
    Task<IReadOnlyList<OrganizationUserResponseDto>> GetOrganizationUsersAsync(Guid organizationId);
    Task<OrganizationUserResponseDto> CreateOrganizationUserAsync(Guid organizationId, OrganizationUserCreateDto dto);
    Task<OrganizationUserResponseDto> UpdateOrganizationUserAsync(Guid organizationId, int userId, OrganizationUserUpdateDto dto);
    Task<bool> RemoveOrganizationUserAsync(Guid organizationId, int userId);
    Task<UserResponseDto> UpdateProfileAsync(int userId, UserProfileUpdateDto dto);
    Task ChangePasswordAsync(int userId, UserChangePasswordDto dto);
    Task<string> UpdateAvatarAsync(int userId, string avatarUrl);
    Task RevokeTokenAsync(string jti, DateTimeOffset expiresAtUtc, int userId);
    Task RevokeRefreshTokensAsync(int userId);
}
