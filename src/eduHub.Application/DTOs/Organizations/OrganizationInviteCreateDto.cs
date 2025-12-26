using eduHub.Domain.Enums;

namespace eduHub.Application.DTOs.Organizations;

public class OrganizationInviteCreateDto
{
    public string Email { get; set; } = string.Empty;
    public OrganizationMemberRole Role { get; set; } = OrganizationMemberRole.User;
    public int? ExpiresInDays { get; set; }
}
