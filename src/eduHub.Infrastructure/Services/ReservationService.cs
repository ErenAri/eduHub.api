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

        public async Task<CursorPageResult<ReservationResponseDto>> SearchAsync(
            ReservationQueryParameters queryParams,
            int? currentUserId,
            bool isAdmin)
        {
            if (!isAdmin && !currentUserId.HasValue)
                throw new UnauthorizedAccessException("Admin access required.");

            var pageSize = ClampPageSize(queryParams.PageSize);

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
            {
                var startUtc = queryParams.StartTimeUtc.Value.ToUniversalTime();
                query = query.Where(r => r.EndTimeUtc >= startUtc);
            }

            if (queryParams.EndTimeUtc.HasValue)
            {
                var endUtc = queryParams.EndTimeUtc.Value.ToUniversalTime();
                query = query.Where(r => r.StartTimeUtc <= endUtc);
            }

            if (!isAdmin && currentUserId.HasValue)
                query = query.Where(r => r.CreatedByUserId == currentUserId.Value);

            var sort = queryParams.Sort?.ToLowerInvariant();
            var isDesc = sort == "start_desc";
            query = isDesc
                ? query.OrderByDescending(r => r.StartTimeUtc).ThenByDescending(r => r.Id)
                : query.OrderBy(r => r.StartTimeUtc).ThenBy(r => r.Id);

            ReservationCursor? cursor = null;
            var cursorProvided = !string.IsNullOrWhiteSpace(queryParams.Cursor);
            if (cursorProvided && !CursorSerializer.TryDecode(queryParams.Cursor, out cursor))
                throw new InvalidOperationException("Invalid cursor.");

            if (cursor != null)
            {
                if (cursor.IsDescending != isDesc)
                    throw new InvalidOperationException("Cursor sort does not match requested sort.");

                query = isDesc
                    ? query.Where(r =>
                        r.StartTimeUtc < cursor.StartUtc ||
                        (r.StartTimeUtc == cursor.StartUtc && r.Id < cursor.Id))
                    : query.Where(r =>
                        r.StartTimeUtc > cursor.StartUtc ||
                        (r.StartTimeUtc == cursor.StartUtc && r.Id > cursor.Id));
            }

            var reservations = await query
                .Take(pageSize + 1)
                .ToListAsync();

            var hasMore = reservations.Count > pageSize;
            if (hasMore)
                reservations = reservations.Take(pageSize).ToList();

            var nextCursor = hasMore
                ? CursorSerializer.Encode(new ReservationCursor(reservations.Last().StartTimeUtc, reservations.Last().Id, isDesc))
                : null;

            var dtos = reservations.Select(MapToDto).ToList();
            if (!isAdmin)
            {
                foreach (var dto in dtos)
                    dto.CreatedByUserId = null;
            }

            return new CursorPageResult<ReservationResponseDto>
            {
                Items = dtos,
                PageSize = pageSize,
                NextCursor = nextCursor,
                HasMore = hasMore
            };
        }

        public async Task<ReservationResponseDto> CreateAsync(
            ReservationCreateDto dto,
            int createdByUserId)
        {
            var startUtc = dto.StartTimeUtc.ToUniversalTime();
            var endUtc = dto.EndTimeUtc.ToUniversalTime();

            await EnsureNoConflicts(dto.RoomId, startUtc, endUtc, null);

            var reservation = new Reservation
            {
                RoomId = dto.RoomId,
                StartTimeUtc = startUtc,
                EndTimeUtc = endUtc,
                Purpose = dto.Purpose,
                Status = ReservationStatus.Pending, // Default status, adjust as needed
                CreatedByUserId = createdByUserId,
                CreatedAtUtc = DateTimeOffset.UtcNow
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
            var startUtc = dto.StartTimeUtc.ToUniversalTime();
            var endUtc = dto.EndTimeUtc.ToUniversalTime();

            if (hasNewRoom)
            {
                var roomExists = await _context.Rooms.AnyAsync(r => r.Id == targetRoomId);
                if (!roomExists)
                    throw new InvalidOperationException("Room does not exist.");
            }

            await EnsureNoConflicts(targetRoomId, startUtc, endUtc, reservation.Id);

            reservation.RoomId = targetRoomId;
            reservation.StartTimeUtc = startUtc;
            reservation.EndTimeUtc = endUtc;
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

            reservation.IsDeleted = true;
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
            DateTimeOffset startUtc,
            DateTimeOffset endUtc,
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

        private static int ClampPageSize(int pageSize)
        {
            if (pageSize < 1) return 10;
            if (pageSize > 100) return 100;
            return pageSize;
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

        private record ReservationCursor(DateTimeOffset StartUtc, int Id, bool IsDescending);
    }
}
