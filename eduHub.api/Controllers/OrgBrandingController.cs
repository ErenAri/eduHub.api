using eduHub.Application.DTOs.Organizations;
using eduHub.Application.Interfaces.Organizations;
using eduHub.Application.Interfaces.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/org/branding")]
public class OrgBrandingController : ApiControllerBase
{
    private readonly IOrganizationService _organizationService;
    private readonly ICurrentTenant _tenant;

    public OrgBrandingController(IOrganizationService organizationService, ICurrentTenant tenant)
    {
        _organizationService = organizationService;
        _tenant = tenant;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationBrandingDto>> GetBranding()
    {
        if (!_tenant.OrganizationId.HasValue)
            return NotFoundProblem("Tenant not found.");

        var org = await _organizationService.GetByIdAsync(_tenant.OrganizationId.Value);
        if (org == null)
            return NotFoundProblem("Tenant not found.");

        return Ok(new OrganizationBrandingDto
        {
            Name = org.Name,
            Slug = org.Slug,
            LogoUrl = org.LogoUrl,
            PrimaryColor = org.PrimaryColor,
            Timezone = org.Timezone
        });
    }
}
