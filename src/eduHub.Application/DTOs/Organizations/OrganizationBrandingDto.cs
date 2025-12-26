namespace eduHub.Application.DTOs.Organizations;

public class OrganizationBrandingDto
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? Timezone { get; set; }
}
