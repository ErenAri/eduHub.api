namespace eduHub.Application.DTOs.Users;

public class ResolvedTenantLoginResult
{
    public AuthResponseDto? Auth { get; set; }
    public bool HasMultipleOrganizations { get; set; }
}
