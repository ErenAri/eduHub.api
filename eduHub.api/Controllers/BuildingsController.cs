using System.Security.Claims;
using eduHub.Application.Common;
using eduHub.Application.DTOs.Buildings;
using eduHub.Application.Interfaces.Buildings;
using eduHub.Application.Security;
using eduHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/org/buildings")]
[Authorize(Policy = AuthorizationConstants.Policies.OrgUser)]
public class BuildingsController : ApiControllerBase
{
    private readonly IBuildingService _buildingService;

    public BuildingsController(IBuildingService buildingService)
    {
        _buildingService = buildingService;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CursorPageResponse<BuildingResponseDto>>> GetBuildings(
        [FromQuery] int pageSize = 20,
        [FromQuery] string? cursor = null)
    {
        var result = await _buildingService.GetPagedAsync(pageSize, cursor);

        var response = new CursorPageResponse<BuildingResponseDto>
        {
            PageSize = result.PageSize,
            NextCursor = result.NextCursor,
            HasMore = result.HasMore,
            Items = result.Items.Select(b => new BuildingResponseDto
            {
                Id = b.Id,
                Name = b.Name
            })
        };

        return Ok(response);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BuildingResponseDto>> GetBuildingById(int id)
    {
        var building = await _buildingService.GetByIdAsync(id);
        if (building == null)
            return NotFoundProblem();

        var response = new BuildingResponseDto
        {
            Id = building.Id,
            Name = building.Name
        };

        return Ok(response);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationConstants.Policies.OrgAdmin)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BuildingResponseDto>> CreateBuilding([FromBody] BuildingCreateDto dto)
    {
        var building = new Building
        {
            Name = dto.Name
        };

        var created = await _buildingService.CreateAsync(building, GetCurrentUserId());

        var response = new BuildingResponseDto
        {
            Id = created.Id,
            Name = created.Name
        };

        return CreatedAtAction(nameof(GetBuildingById), new { id = response.Id }, response);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthorizationConstants.Policies.OrgAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BuildingResponseDto>> UpdateBuilding(int id, [FromBody] BuildingUpdateDto dto)
    {
        if (id != dto.Id)
            return BadRequestProblem("Route id and body id do not match.");

        var building = await _buildingService.GetByIdAsync(id);
        if (building == null)
            return NotFoundProblem();

        building.Name = dto.Name;

        var updated = await _buildingService.UpdateAsync(building, GetCurrentUserId());

        var response = new BuildingResponseDto
        {
            Id = updated.Id,
            Name = updated.Name
        };

        return Ok(response);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AuthorizationConstants.Policies.OrgAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteBuilding(int id)
    {
        var building = await _buildingService.GetByIdAsync(id);
        if (building == null)
            return NotFoundProblem();

        await _buildingService.DeleteAsync(id, GetCurrentUserId());
        return NoContent();
    }

    private int GetCurrentUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(idClaim) || !int.TryParse(idClaim, out var userId))
            throw new UnauthorizedAccessException("User id claim is missing.");

        return userId;
    }
}
