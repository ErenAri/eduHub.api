using System.Security.Claims;
using System.Security.Cryptography;
using eduHub.Application.DTOs.Organizations;
using eduHub.Application.Interfaces.Organizations;
using eduHub.Application.Security;
using eduHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/platform/organizations")]
[Authorize(Policy = AuthorizationConstants.Policies.PlatformAdmin)]
public class PlatformOrganizationsController : ApiControllerBase
{
    private const int OrgSlugLength = 8;
    private const int MaxSlugAttempts = 10;
    private static readonly char[] OrgSlugAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();
    private readonly IOrganizationService _organizationService;

    public PlatformOrganizationsController(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<OrganizationResponseDto>>> GetAll()
    {
        var orgs = await _organizationService.GetAllAsync();
        return Ok(orgs.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationResponseDto>> GetById(Guid id)
    {
        var org = await _organizationService.GetByIdAsync(id);
        if (org == null)
            return NotFoundProblem("Organization not found.");

        return Ok(ToResponse(org));
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrganizationResponseDto>> Create([FromBody] OrganizationCreateDto dto)
    {
        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequestProblem("Name is required.");

        var slug = dto.Slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = await GenerateUniqueSlugAsync();
        }
        else
        {
            if (!IsValidSlug(slug))
                return BadRequestProblem("Organization ID must be 8 characters of lowercase letters and numbers.");

            var existing = await _organizationService.GetBySlugAsync(slug);
            if (existing != null)
                return ConflictProblem("Organization ID already exists.");
        }

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            LogoUrl = string.IsNullOrWhiteSpace(dto.LogoUrl) ? null : dto.LogoUrl.Trim(),
            PrimaryColor = string.IsNullOrWhiteSpace(dto.PrimaryColor) ? null : dto.PrimaryColor.Trim(),
            Timezone = string.IsNullOrWhiteSpace(dto.Timezone) ? null : dto.Timezone.Trim(),
            SubscriptionPlan = string.IsNullOrWhiteSpace(dto.SubscriptionPlan) ? null : dto.SubscriptionPlan.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = GetCurrentUserId()
        };

        var created = await _organizationService.CreateAsync(org);
        var response = ToResponse(created);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationResponseDto>> UpdateStatus(
        Guid id,
        [FromBody] OrganizationStatusUpdateDto dto)
    {
        var updated = await _organizationService.UpdateStatusAsync(id, dto.IsActive);
        return Ok(ToResponse(updated));
    }

    [HttpPatch("{id:guid}/plan")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationResponseDto>> UpdatePlan(
        Guid id,
        [FromBody] OrganizationPlanUpdateDto dto)
    {
        var updated = await _organizationService.UpdatePlanAsync(id, dto.SubscriptionPlan?.Trim() ?? string.Empty);
        return Ok(ToResponse(updated));
    }

    private static OrganizationResponseDto ToResponse(Organization org)
    {
        return new OrganizationResponseDto
        {
            Id = org.Id,
            Name = org.Name,
            Slug = org.Slug,
            LogoUrl = org.LogoUrl,
            PrimaryColor = org.PrimaryColor,
            Timezone = org.Timezone,
            SubscriptionPlan = org.SubscriptionPlan,
            IsActive = org.IsActive,
            CreatedAtUtc = org.CreatedAtUtc
        };
    }

    private async Task<string> GenerateUniqueSlugAsync()
    {
        for (var attempt = 0; attempt < MaxSlugAttempts; attempt++)
        {
            var slug = GenerateSlug();
            if (await _organizationService.GetBySlugAsync(slug) == null)
                return slug;
        }

        throw new InvalidOperationException("Failed to generate a unique organization ID.");
    }

    private static string GenerateSlug()
    {
        Span<char> buffer = stackalloc char[OrgSlugLength];
        for (var i = 0; i < buffer.Length; i++)
        {
            var index = RandomNumberGenerator.GetInt32(OrgSlugAlphabet.Length);
            buffer[i] = OrgSlugAlphabet[index];
        }

        return new string(buffer);
    }

    private static bool IsValidSlug(string slug)
    {
        if (slug.Length != OrgSlugLength)
            return false;

        foreach (var ch in slug)
        {
            var isLowerAlpha = ch is >= 'a' and <= 'z';
            var isDigit = ch is >= '0' and <= '9';
            if (!isLowerAlpha && !isDigit)
                return false;
        }

        return true;
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(idClaim))
            return null;

        return int.TryParse(idClaim, out var userId) ? userId : null;
    }
}
