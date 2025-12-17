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
            var userId = GetCurrentUserId();
            var isAdmin = IsCurrentUserAdmin();
            var reservation = await _reservationService.GetByIdAsync(id, userId, isAdmin);
            if (reservation == null)
                return NotFound();

            return Ok(reservation);
        }

        [HttpGet("search")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<PagedResponse<ReservationResponseDto>>> Search(
            [FromQuery] ReservationQueryParameters query)
        {
            var isAdmin = IsCurrentUserAdmin();
            var result = await _reservationService.SearchAsync(query, currentUserId: null, isAdmin: isAdmin);
            return Ok(ToResponse(result));
        }

        [HttpGet("mine")]
        public async Task<ActionResult<PagedResponse<ReservationResponseDto>>> GetMine(
            [FromQuery] ReservationQueryParameters query)
        {
            var userId = GetCurrentUserId();
            var result = await _reservationService.SearchAsync(query, currentUserId: userId, isAdmin: false);
            return Ok(ToResponse(result));
        }

        [HttpGet("room/{roomId:int}")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<PagedResponse<ReservationResponseDto>>> GetByRoom(
            int roomId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var isAdmin = IsCurrentUserAdmin();
            var query = new ReservationQueryParameters
            {
                RoomId = roomId,
                Page = page,
                PageSize = pageSize
            };

            var result = await _reservationService.SearchAsync(query, currentUserId: null, isAdmin: isAdmin);
            return Ok(ToResponse(result));
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

        [HttpPost("{id:int}/approve")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ReservationResponseDto>> Approve(int id)
        {
            var isAdmin = IsCurrentUserAdmin();
            var reservation = await _reservationService.ApproveAsync(id, isAdmin: isAdmin);
            return Ok(reservation);
        }

        [HttpPost("{id:int}/reject")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ReservationResponseDto>> Reject(int id)
        {
            var isAdmin = IsCurrentUserAdmin();
            var reservation = await _reservationService.RejectAsync(id, isAdmin: isAdmin);
            return Ok(reservation);
        }

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(idClaim))
                throw new UnauthorizedAccessException("User id claim is missing.");

            if (!int.TryParse(idClaim, out var userId))
                throw new UnauthorizedAccessException("User id claim is invalid.");

            return userId;
        }

        private bool IsCurrentUserAdmin()
        {
            return User.IsInRole("Admin");
        }

        private static PagedResponse<ReservationResponseDto> ToResponse(PagedResult<ReservationResponseDto> result)
        {
            return new PagedResponse<ReservationResponseDto>
            {
                Items = result.Items,
                Page = result.Page,
                PageSize = result.PageSize,
                TotalCount = result.TotalCount,
                TotalPages = result.TotalPages
            };
        }
    }
}
