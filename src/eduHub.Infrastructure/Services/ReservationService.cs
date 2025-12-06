using eduHub.Application.Common;
using eduHub.Application.DTOs.Reservations;
using eduHub.Application.Interfaces.Reservations;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eduHub.Infrastructure.Services
{
    public class ReservationService : IReservationService
    {
        private readonly AppDbContext _context;

        public ReservationService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<ReservationResponseDto?> GetByIdAsync(int id)
        {
            var reservation = await _context.Reservations
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);

            return reservation == null ? null : MapToDto(reservation);
        }

        public async Task<PagedResult<ReservationResponseDto>> GetByRoomIdPagedAsync(
            int roomId,
            int page,
            int pageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var query = _context.Reservations
                .AsNoTracking()
                .Where(r => r.RoomId == roomId)
                .OrderBy(r => r.StartTimeUtc);

            var totalCount = await query.CountAsync();

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<ReservationResponseDto>
            {
                Items = items.Select(MapToDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<PagedResult<ReservationResponseDto>> SearchAsync(
            int? buildingId,
            int? roomId,
            DateTime? startTimeUtc,
            DateTime? endTimeUtc,
            int page,
            int pageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var query = _context.Reservations
                .AsNoTracking()
                .Include(r => r.Room)
                .ThenInclude(r => r.Building)
                .AsQueryable();

            if (roomId.HasValue)
                query = query.Where(r => r.RoomId == roomId.Value);

            if (buildingId.HasValue)
                query = query.Where(r => r.Room != null && r.Room.BuildingId == buildingId.Value);

            if (startTimeUtc.HasValue)
                query = query.Where(r => r.EndTimeUtc >= startTimeUtc.Value);

            if (endTimeUtc.HasValue)
                query = query.Where(r => r.StartTimeUtc <= endTimeUtc.Value);

            query = query.OrderBy(r => r.StartTimeUtc);

            var totalCount = await query.CountAsync();

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<ReservationResponseDto>
            {
                Items = items.Select(MapToDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<List<ReservationResponseDto>> GetByRoomAsync(int roomId)
        {
            var reservations = await _context.Reservations
                .AsNoTracking()
                .Where(r => r.RoomId == roomId)
                .OrderBy(r => r.StartTimeUtc)
                .ToListAsync();

            return reservations.Select(MapToDto).ToList();
        }

        public async Task<ReservationResponseDto> CreateAsync(
            ReservationCreateDto dto,
            int createdByUserId)
        {
            await EnsureNoConflicts(dto.RoomId, dto.StartTimeUtc, dto.EndTimeUtc, null);

            var reservation = new Reservation
            {
                RoomId = dto.RoomId,
                StartTimeUtc = dto.StartTimeUtc,
                EndTimeUtc = dto.EndTimeUtc,
                Purpose = dto.Purpose,
                Status = ReservationStatus.Pending, // Default status, adjust as needed
                CreatedByUserId = createdByUserId,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.Reservations.Add(reservation);
            await _context.SaveChangesAsync();

            return MapToDto(reservation);
        }

        public async Task<ReservationResponseDto> UpdateAsync(
            int id,
            ReservationUpdateDto dto,
            int currentUserId,
            bool isAdmin)
        {
            var reservation = await _context.Reservations
                .FirstOrDefaultAsync(r => r.Id == id);

            if (reservation == null)
                throw new KeyNotFoundException("Reservation not found.");

            if (!isAdmin && reservation.CreatedByUserId != currentUserId)
                throw new UnauthorizedAccessException("You are not allowed to modify this reservation.");

            await EnsureNoConflicts(reservation.RoomId, dto.StartTimeUtc, dto.EndTimeUtc, reservation.Id);

            reservation.StartTimeUtc = dto.StartTimeUtc;
            reservation.EndTimeUtc = dto.EndTimeUtc;
            reservation.Purpose = dto.Purpose;

            await _context.SaveChangesAsync();

            return MapToDto(reservation);
        }

        public async Task<bool> DeleteAsync(
            int id,
            int currentUserId,
            bool isAdmin)
        {
            var reservation = await _context.Reservations
                .FirstOrDefaultAsync(r => r.Id == id);

            if (reservation == null)
                return false;

            if (!isAdmin && reservation.CreatedByUserId != currentUserId)
                throw new UnauthorizedAccessException("You are not allowed to delete this reservation.");

            _context.Reservations.Remove(reservation);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<PagedResult<ReservationResponseDto>> GetMyReservationsAsync(
            int currentUserId,
            int page,
            int pageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var query = _context.Reservations
                .AsNoTracking()
                .Where(r => r.CreatedByUserId == currentUserId)
                .OrderBy(r => r.StartTimeUtc);

            var totalCount = await query.CountAsync();

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<ReservationResponseDto>
            {
                Items = items.Select(MapToDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        private async Task EnsureNoConflicts(
            int roomId,
            DateTime startUtc,
            DateTime endUtc,
            int? excludeReservationId)
        {
            if (endUtc <= startUtc)
                throw new InvalidOperationException("End time must be after start time.");

            var query = _context.Reservations
                .AsNoTracking()
                .Where(r => r.RoomId == roomId);

            if (excludeReservationId.HasValue)
                query = query.Where(r => r.Id != excludeReservationId.Value);

            var hasConflict = await query.AnyAsync(r =>
                r.StartTimeUtc < endUtc &&
                startUtc < r.EndTimeUtc);

            if (hasConflict)
                throw new InvalidOperationException("The room is already reserved in the given time range.");
        }

        private static ReservationResponseDto MapToDto(Reservation reservation)
        {
            return new ReservationResponseDto
            {
                Id = reservation.Id,
                RoomId = reservation.RoomId,
                Start = reservation.StartTimeUtc,
                End = reservation.EndTimeUtc,
                Purpose = reservation.Purpose,
                Status = reservation.Status.ToString(), 
                CreatedByUserId = reservation.CreatedByUserId,
                CreatedAtUtc = reservation.CreatedAtUtc
            };
        }
    }
}
