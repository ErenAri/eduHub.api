namespace eduHub.Application.DTOs.Organizations;

public class OrganizationInviteRedeemDto
{
    public string Token { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string Password { get; set; } = string.Empty;
}
