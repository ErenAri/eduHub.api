using System.Security.Claims;
using eduHub.Application.DTOs.Organizations;
using eduHub.Application.Interfaces.Organizations;
using eduHub.Application.Interfaces.Tenants;
using eduHub.Application.Security;
using eduHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/org/invites")]
[Authorize(Policy = AuthorizationConstants.Policies.OrgAdmin)]
public class OrgInvitesController : ApiControllerBase
{
    private readonly IOrganizationInviteService _inviteService;
    private readonly ICurrentTenant _tenant;

    public OrgInvitesController(IOrganizationInviteService inviteService, ICurrentTenant tenant)
    {
        _inviteService = inviteService;
        _tenant = tenant;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrganizationInviteResponseDto>> Create([FromBody] OrganizationInviteCreateDto dto)
    {
        if (!_tenant.OrganizationId.HasValue)
            return NotFoundProblem("Tenant not found.");

        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequestProblem("Email is required.");

        var (invite, token) = await _inviteService.CreateAsync(
            _tenant.OrganizationId.Value,
            dto,
            GetCurrentUserId());

        return Ok(ToResponse(invite, token));
    }

    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid id)
    {
        await _inviteService.RevokeAsync(id, GetCurrentUserId());
        return NoContent();
    }

    private int GetCurrentUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var userId))
            throw new UnauthorizedAccessException("User id claim is missing.");

        return userId;
    }

    private static OrganizationInviteResponseDto ToResponse(OrganizationInvite invite, string? token = null)
    {
        return new OrganizationInviteResponseDto
        {
            Id = invite.Id,
            Email = invite.Email,
            Role = invite.Role,
            ExpiresAtUtc = invite.ExpiresAtUtc,
            CreatedAtUtc = invite.CreatedAtUtc,
            UsedAtUtc = invite.UsedAtUtc,
            RevokedAtUtc = invite.RevokedAtUtc,
            Token = token
        };
    }
}
