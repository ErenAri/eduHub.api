using eduHub.Application.Common;
using eduHub.Application.DTOs.Buildings;
using eduHub.Application.Interfaces.Buildings;
using eduHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class BuildingsController : ControllerBase
{
    private readonly IBuildingService _buildingService;

    public BuildingsController(IBuildingService buildingService)
    {
        _buildingService = buildingService;
    }

    // GET api/Buildings?page=&pageSize=
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]

    public async Task<ActionResult<PagedResponse<BuildingResponseDto>>> GetBuildings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _buildingService.GetPagedAsync(page, pageSize);

        var response = new PagedResponse<BuildingResponseDto>
        {
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            TotalPages = result.TotalPages,
            Items = result.Items.Select(b => new BuildingResponseDto
            {
                Id = b.Id,
                Name = b.Name
            })
        };

        return Ok(response);
    }

    // GET api/Buildings/5
    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BuildingResponseDto>> GetBuildingById(int id)
    {
        var building = await _buildingService.GetByIdAsync(id);
        if (building == null)
            return NotFound();

        var response = new BuildingResponseDto
        {
            Id = building.Id,
            Name = building.Name
        };

        return Ok(response);
    }

    // POST api/Buildings
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BuildingResponseDto>> CreateBuilding([FromBody] BuildingCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var building = new Building
        {
            Name = dto.Name
        };

        var created = await _buildingService.CreateAsync(building);

        var response = new BuildingResponseDto
        {
            Id = created.Id,
            Name = created.Name
        };

        return CreatedAtAction(nameof(GetBuildingById), new { id = response.Id }, response);
    }

    // PUT api/Buildings/5
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BuildingResponseDto>> UpdateBuilding(int id, [FromBody] BuildingUpdateDto dto)
    {
        if (id != dto.Id)
            return BadRequest("Route id and body id do not match.");

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var building = await _buildingService.GetByIdAsync(id);
        if (building == null)
            return NotFound();

        building.Name = dto.Name;

        var updated = await _buildingService.UpdateAsync(building);

        var response = new BuildingResponseDto
        {
            Id = updated.Id,
            Name = updated.Name
        };

        return Ok(response);
    }

    // DELETE api/Buildings/5
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteBuilding(int id)
    {
        var building = await _buildingService.GetByIdAsync(id);
        if (building == null)
            return NotFound();

        await _buildingService.DeleteAsync(id);
        return NoContent();
    }
}
