namespace eduHub.Application.DTOs.Users;

public class UserLoginDto
{
    public string UserNameOrEmail { get; set; } = null!;
    public string Password { get; set; } = null!;
}
