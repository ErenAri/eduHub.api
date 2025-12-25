namespace eduHub.Application.DTOs.Users;

public class UserChangePasswordDto
{
    public string CurrentPassword { get; set; } = null!;
    public string NewPassword { get; set; } = null!;
}
