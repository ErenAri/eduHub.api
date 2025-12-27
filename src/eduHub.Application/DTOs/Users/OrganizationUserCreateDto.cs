using eduHub.Domain.Enums;

namespace eduHub.Application.DTOs.Users;

public class OrganizationUserCreateDto
{
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;
    public OrganizationMemberRole Role { get; set; } = OrganizationMemberRole.User;
}
