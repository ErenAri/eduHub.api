namespace eduHub.Application.DTOs.Tenants;

public class TenantResolveRequestResponseDto
{
    public bool VerificationSent { get; set; }
    public string? DebugToken { get; set; }
}
