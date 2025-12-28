using System.Security.Claims;
using eduHub.Application.DTOs.Availability;
using eduHub.Application.Interfaces.Availability;
using eduHub.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/org/availability")]
[Authorize(Policy = AuthorizationConstants.Policies.OrgUser)]
public class AvailabilityController : ApiControllerBase
{
    private readonly IAvailabilityService _availabilityService;

    public AvailabilityController(IAvailabilityService availabilityService)
    {
        _availabilityService = availabilityService;
    }

    [HttpGet("buildings/{buildingId:int}/hours")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AvailabilityWindowDto>>> GetBuildingHours(
        int buildingId)
    {
        var hours = await _availabilityService.GetBuildingHoursAsync(buildingId);
        return Ok(hours);
    }

    [HttpPut("buildings/{buildingId:int}/hours")]
    [Authorize(Policy = AuthorizationConstants.Policies.OrgAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AvailabilityWindowDto>>> UpdateBuildingHours(
        int buildingId,
        [FromBody] List<AvailabilityWindowDto>? windows)
    {
        var updated = await _availabilityService.UpdateBuildingHoursAsync(
            buildingId,
            windows ?? new List<AvailabilityWindowDto>(),
            GetCurrentUserId());
        return Ok(updated);
    }

    [HttpGet("rooms/{roomId:int}/hours")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AvailabilityWindowDto>>> GetRoomHours(
        int roomId)
    {
        var hours = await _availabilityService.GetRoomHoursAsync(roomId);
        return Ok(hours);
    }

    [HttpPut("rooms/{roomId:int}/hours")]
    [Authorize(Policy = AuthorizationConstants.Policies.OrgAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AvailabilityWindowDto>>> UpdateRoomHours(
        int roomId,
        [FromBody] List<AvailabilityWindowDto>? windows)
    {
        var updated = await _availabilityService.UpdateRoomHoursAsync(
            roomId,
            windows ?? new List<AvailabilityWindowDto>(),
            GetCurrentUserId());
        return Ok(updated);
    }

    [HttpGet("rooms/{roomId:int}/slots")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AvailabilitySlotDto>>> GetRoomAvailability(
        int roomId,
        [FromQuery] DateTimeOffset startTimeUtc,
        [FromQuery] DateTimeOffset endTimeUtc)
    {
        var slots = await _availabilityService.GetRoomAvailabilityAsync(
            roomId,
            startTimeUtc,
            endTimeUtc);
        return Ok(slots);
    }

    [HttpGet("blackouts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AvailabilityBlackoutResponseDto>>> GetBlackouts(
        [FromQuery] int? buildingId,
        [FromQuery] int? roomId,
        [FromQuery] DateTimeOffset? startTimeUtc,
        [FromQuery] DateTimeOffset? endTimeUtc)
    {
        var blackouts = await _availabilityService.GetBlackoutsAsync(
            buildingId,
            roomId,
            startTimeUtc,
            endTimeUtc);
        return Ok(blackouts);
    }

    [HttpPost("blackouts")]
    [Authorize(Policy = AuthorizationConstants.Policies.OrgAdmin)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<AvailabilityBlackoutResponseDto>> CreateBlackout(
        [FromBody] AvailabilityBlackoutCreateDto dto)
    {
        var blackout = await _availabilityService.CreateBlackoutAsync(dto, GetCurrentUserId());
        return StatusCode(StatusCodes.Status201Created, blackout);
    }

    [HttpDelete("blackouts/{id:int}")]
    [Authorize(Policy = AuthorizationConstants.Policies.OrgAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBlackout(int id)
    {
        var deleted = await _availabilityService.DeleteBlackoutAsync(id, GetCurrentUserId());
        if (!deleted)
            return NotFoundProblem();

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
