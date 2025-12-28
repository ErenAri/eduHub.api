using eduHub.Application.DTOs.Organizations;
using eduHub.Application.Interfaces.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/public/organizations")]
public class PublicOrganizationsController : ApiControllerBase
{
    private readonly IOrganizationService _organizationService;

    public PublicOrganizationsController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<PublicOrganizationDto>>> GetOrganizations(
        [FromQuery] string? query = null,
        [FromQuery] int limit = 50)
    {
        var orgs = await _organizationService.SearchActiveAsync(query, limit);
        var response = orgs.Select(MapPublicOrganization).ToList();
        return Ok(response);
    }

    [HttpGet("{slug}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicOrganizationDto>> GetOrganizationBySlug(
        string slug)
    {
        var normalized = slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return BadRequestProblem("Organization ID is required.");

        var org = await _organizationService.GetActiveBySlugAsync(normalized);
        if (org == null)
            return NotFoundProblem("Organization not found.");

        return Ok(MapPublicOrganization(org));
    }

    private static PublicOrganizationDto MapPublicOrganization(
        eduHub.Domain.Entities.Organization org)
    {
        return new PublicOrganizationDto
        {
            Id = org.Id,
            Name = org.Name,
            Slug = org.Slug,
            LogoUrl = org.LogoUrl,
            PrimaryColor = org.PrimaryColor,
            Timezone = org.Timezone
        };
    }
}
