using eduHub.Application.Common;
using eduHub.Application.DTOs.Rooms;
using eduHub.Application.Interfaces.Rooms;
using eduHub.Domain.Entities;
using eduHub.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class RoomsController : ControllerBase
{
    private readonly IRoomService _roomService;

    public RoomsController(IRoomService roomService)
    {
        _roomService = roomService;
    }

    /// <summary>
    /// Returns a paginated list of rooms for the specified building.
    /// </summary>
    [HttpGet("by-building/{buildingId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<RoomResponseDto>>> GetRoomsByBuilding(
        int buildingId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _roomService.GetByBuildingIdPagedAsync(buildingId, page, pageSize);

        var response = new PagedResponse<RoomResponseDto>
        {
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            TotalPages = result.TotalPages,
            Items = result.Items.Select(r => new RoomResponseDto
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name,
                Capacity = r.Capacity,
                BuildingId = r.BuildingId
            })
        };

        return Ok(response);
    }

    /// <summary>
    /// Returns a single room by its identifier.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomResponseDto>> GetRoomById(int id)
    {
        var room = await _roomService.GetByIdAsync(id);
        if (room == null)
            return NotFound();

        var response = new RoomResponseDto
        {
            Id = room.Id,
            Code = room.Code,
            Name = room.Name,
            Capacity = room.Capacity,
            BuildingId = room.BuildingId
        };

        return Ok(response);
    }

    /// <summary>
    /// Creates a new room.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RoomResponseDto>> CreateRoom([FromBody] RoomCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var room = new Room
        {
            Code = dto.Code,
            Name = dto.Name,
            Capacity = dto.Capacity,
            BuildingId = dto.BuildingId
        };

        var created = await _roomService.CreateAsync(room);

        var response = new RoomResponseDto
        {
            Id = created.Id,
            Code = created.Code,
            Name = created.Name,
            Capacity = created.Capacity,
            BuildingId = created.BuildingId
        };

        return CreatedAtAction(nameof(GetRoomById), new { id = response.Id }, response);
    }

    /// <summary>
    /// Updates an existing room.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomResponseDto>> UpdateRoom(int id, [FromBody] RoomUpdateDto dto)
    {
        if (id != dto.Id)
            return BadRequest("Route id and body id do not match.");

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var room = await _roomService.GetByIdAsync(id);
        if (room == null)
            return NotFound();

        room.Code = dto.Code;
        room.Name = dto.Name;
        room.Capacity = dto.Capacity;

        var updated = await _roomService.UpdateAsync(room);

        var response = new RoomResponseDto
        {
            Id = updated.Id,
            Code = updated.Code,
            Name = updated.Name,
            Capacity = updated.Capacity,
            BuildingId = updated.BuildingId
        };

        return Ok(response);
    }

    /// <summary>
    /// Deletes a room.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRoom(int id)
    {
        var room = await _roomService.GetByIdAsync(id);
        if (room == null)
            return NotFound();

        await _roomService.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>
    /// Returns available rooms in a building for the given time range.
    /// </summary>
    [HttpGet("available")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<RoomResponseDto>>> GetAvailableRooms(
        [FromQuery] int buildingId,
        [FromQuery] DateTime startTimeUtc,
        [FromQuery] DateTime endTimeUtc)
    {
        if (buildingId <= 0)
            return BadRequest("buildingId must be greater than 0.");

        if (startTimeUtc >= endTimeUtc)
            return BadRequest("startTimeUtc must be earlier than endTimeUtc.");

        var rooms = await _roomService.GetAvailableRoomsAsync(buildingId, startTimeUtc, endTimeUtc);

        var response = rooms.Select(r => new RoomResponseDto
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            Capacity = r.Capacity,
            BuildingId = r.BuildingId
        });

        return Ok(response);
    }
}
