using eduHub.Application.DTOs.Users;

namespace eduHub.Application.Interfaces.Users;

public interface IUserService
{
    Task<UserResponseDto> RegisterAsync(UserRegisterDto dto);
    Task<AuthResponseDto?> LoginAsync(UserLoginDto dto);
}
