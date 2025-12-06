using System.Security.Claims;
using eduHub.Application.Common;
using eduHub.Application.DTOs.Reservations;
using eduHub.Application.Interfaces.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eduHub.api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReservationsController : ControllerBase
    {
        private readonly IReservationService _reservationService;

        public ReservationsController(IReservationService reservationService)
        {
            _reservationService = reservationService;
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ReservationResponseDto>> GetById(int id)
        {
            var reservation = await _reservationService.GetByIdAsync(id);
            if (reservation == null)
                return NotFound();

            return Ok(reservation);
        }

        [HttpGet("room/{roomId:int}")]
        public async Task<ActionResult<List<ReservationResponseDto>>> GetByRoom(int roomId)
        {
            var reservations = await _reservationService.GetByRoomAsync(roomId);
            return Ok(reservations);
        }

        [HttpGet("room/{roomId:int}/paged")]
        public async Task<ActionResult<PagedResult<ReservationResponseDto>>> GetByRoomPaged(
            int roomId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _reservationService.GetByRoomIdPagedAsync(roomId, page, pageSize);
            return Ok(result);
        }

        [HttpGet("search")]
        public async Task<ActionResult<PagedResult<ReservationResponseDto>>> Search(
            [FromQuery] int? buildingId,
            [FromQuery] int? roomId,
            [FromQuery] DateTime? startTimeUtc,
            [FromQuery] DateTime? endTimeUtc,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _reservationService.SearchAsync(
                buildingId,
                roomId,
                startTimeUtc,
                endTimeUtc,
                page,
                pageSize);

            return Ok(result);
        }

        [HttpPost]
        public async Task<ActionResult<ReservationResponseDto>> Create([FromBody] ReservationCreateDto dto)
        {
            var userId = GetCurrentUserId();
            var reservation = await _reservationService.CreateAsync(dto, userId);
            return CreatedAtAction(nameof(GetById), new { id = reservation.Id }, reservation);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<ReservationResponseDto>> Update(
            int id,
            [FromBody] ReservationUpdateDto dto)
        {
            var userId = GetCurrentUserId();
            var isAdmin = IsCurrentUserAdmin();

            var reservation = await _reservationService.UpdateAsync(id, dto, userId, isAdmin);
            return Ok(reservation);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = GetCurrentUserId();
            var isAdmin = IsCurrentUserAdmin();

            var deleted = await _reservationService.DeleteAsync(id, userId, isAdmin);
            if (!deleted)
                return NotFound();

            return NoContent();
        }

        [HttpGet("mine")]
        public async Task<ActionResult<PagedResult<ReservationResponseDto>>> GetMine(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var userId = GetCurrentUserId();
            var result = await _reservationService.GetMyReservationsAsync(userId, page, pageSize);
            return Ok(result);
        }

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(idClaim))
                throw new UnauthorizedAccessException("User id claim is missing.");

            return int.Parse(idClaim);
        }

        private bool IsCurrentUserAdmin()
        {
            return User.IsInRole("Admin");
        }
    }
}
