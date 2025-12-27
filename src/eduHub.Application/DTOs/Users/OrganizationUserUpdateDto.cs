using eduHub.Domain.Enums;

namespace eduHub.Application.DTOs.Users;

public class OrganizationUserUpdateDto
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public OrganizationMemberRole? Role { get; set; }
    public OrganizationMemberStatus? Status { get; set; }
}
