using eduHub.Application.Common;
using eduHub.Application.DTOs.Reservations;
using eduHub.Application.Interfaces.Reservations;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace eduHub.Infrastructure.Services
{
    public class ReservationService : IReservationService
    {
        private readonly AppDbContext _context;

        public ReservationService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<ReservationResponseDto?> GetByIdAsync(int id, int currentUserId, bool isAdmin)
        {
            var query = _context.Reservations.AsNoTracking().Where(r => r.Id == id);
            if (!isAdmin)
                query = query.Where(r => r.CreatedByUserId == currentUserId);

            var reservation = await query.FirstOrDefaultAsync();
            if (reservation == null)
                return null;

            var dto = MapToDto(reservation);
            if (!isAdmin)
                dto.CreatedByUserId = null;

            return dto;
        }

        public async Task<PagedResult<ReservationResponseDto>> SearchAsync(
            ReservationQueryParameters queryParams,
            int? currentUserId,
            bool isAdmin)
        {
            if (!isAdmin && !currentUserId.HasValue)
                throw new UnauthorizedAccessException("Admin access required.");

            var page = queryParams.Page < 1 ? 1 : queryParams.Page;
            var pageSize = queryParams.PageSize < 1 ? 10 : Math.Min(queryParams.PageSize, 100);

            var query = _context.Reservations
                .AsNoTracking()
                .Include(r => r.Room)
                .ThenInclude(r => r.Building)
                .AsQueryable();

            if (queryParams.RoomId.HasValue)
                query = query.Where(r => r.RoomId == queryParams.RoomId.Value);

            if (queryParams.BuildingId.HasValue)
                query = query.Where(r => r.Room != null && r.Room.BuildingId == queryParams.BuildingId.Value);

            if (queryParams.StartTimeUtc.HasValue)
                query = query.Where(r => r.EndTimeUtc >= queryParams.StartTimeUtc.Value);

            if (queryParams.EndTimeUtc.HasValue)
                query = query.Where(r => r.StartTimeUtc <= queryParams.EndTimeUtc.Value);

            if (!isAdmin && currentUserId.HasValue)
                query = query.Where(r => r.CreatedByUserId == currentUserId.Value);

            var sort = queryParams.Sort?.ToLowerInvariant();
            var isDesc = sort == "start_desc";
            query = isDesc
                ? query.OrderByDescending(r => r.StartTimeUtc).ThenByDescending(r => r.Id)
                : query.OrderBy(r => r.StartTimeUtc).ThenBy(r => r.Id);

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
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23P01" })
            {
                throw new InvalidOperationException("The room is already reserved in the given time range.");
            }

            return MapToDto(reservation);
        }

        public async Task<ReservationResponseDto> UpdateAsync(
            int id,
            ReservationUpdateDto dto,
            int currentUserId,
            bool isAdmin)
        {
            var reservation = await _context.Reservations.FirstOrDefaultAsync(r =>
                r.Id == id && (isAdmin || r.CreatedByUserId == currentUserId));

            if (reservation == null)
                throw new KeyNotFoundException("Reservation not found.");

            var hasNewRoom = dto.RoomId != default && dto.RoomId != reservation.RoomId;
            var targetRoomId = hasNewRoom ? dto.RoomId : reservation.RoomId;

            if (hasNewRoom)
            {
                var roomExists = await _context.Rooms.AnyAsync(r => r.Id == targetRoomId);
                if (!roomExists)
                    throw new InvalidOperationException("Room does not exist.");
            }

            await EnsureNoConflicts(targetRoomId, dto.StartTimeUtc, dto.EndTimeUtc, reservation.Id);

            reservation.RoomId = targetRoomId;
            reservation.StartTimeUtc = dto.StartTimeUtc;
            reservation.EndTimeUtc = dto.EndTimeUtc;
            reservation.Purpose = dto.Purpose;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23P01" })
            {
                throw new InvalidOperationException("The room is already reserved in the given time range.");
            }

            return MapToDto(reservation);
        }

        public async Task<bool> DeleteAsync(
            int id,
            int currentUserId,
            bool isAdmin)
        {
            var reservation = await _context.Reservations.FirstOrDefaultAsync(r =>
                r.Id == id && (isAdmin || r.CreatedByUserId == currentUserId));

            if (reservation == null)
                return false;

            _context.Reservations.Remove(reservation);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<ReservationResponseDto> ApproveAsync(int id, bool isAdmin)
        {
            if (!isAdmin)
                throw new UnauthorizedAccessException("Forbidden.");
            var reservation = await _context.Reservations
                .FirstOrDefaultAsync(r => r.Id == id);

            if (reservation == null)
                throw new KeyNotFoundException("Reservation not found.");

            if (reservation.Status == ReservationStatus.Approved)
                return MapToDto(reservation);

            if (reservation.Status != ReservationStatus.Pending)
                throw new InvalidOperationException("Only pending reservations can be approved.");

            reservation.Status = ReservationStatus.Approved;
            await _context.SaveChangesAsync();

            return MapToDto(reservation);
        }

        public async Task<ReservationResponseDto> RejectAsync(int id, bool isAdmin)
        {
            if (!isAdmin)
                throw new UnauthorizedAccessException("Forbidden.");
            var reservation = await _context.Reservations
                .FirstOrDefaultAsync(r => r.Id == id);

            if (reservation == null)
                throw new KeyNotFoundException("Reservation not found.");

            if (reservation.Status == ReservationStatus.Rejected)
                return MapToDto(reservation);

            if (reservation.Status != ReservationStatus.Pending)
                throw new InvalidOperationException("Only pending reservations can be rejected.");

            reservation.Status = ReservationStatus.Rejected;
            await _context.SaveChangesAsync();

            return MapToDto(reservation);
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
                .Where(r => r.RoomId == roomId &&
                            (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Approved));

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
