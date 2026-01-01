using eduHub.Application.Common;
using eduHub.Application.DTOs.Reservations;
using eduHub.Application.Interfaces.Availability;
using eduHub.Application.Interfaces.Reservations;
using eduHub.Application.Options;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace eduHub.Infrastructure.Services
{
    public class ReservationService : IReservationService
    {
        private readonly AppDbContext _context;
        private readonly IAvailabilityService _availabilityService;
        private readonly ReservationPolicyOptions _policy;

        public ReservationService(
            AppDbContext context,
            IAvailabilityService availabilityService,
            IOptions<ReservationPolicyOptions> policyOptions)
        {
            _context = context;
            _availabilityService = availabilityService;
            _policy = policyOptions.Value;
        }

        public async Task<ReservationResponseDto?> GetByIdAsync(int id, int currentUserId, bool canViewAll)
        {
            var query = _context.Reservations.AsNoTracking().Where(r => r.Id == id);
            if (!canViewAll)
                query = query.Where(r => r.CreatedByUserId == currentUserId);

            var reservation = await query.FirstOrDefaultAsync();
            if (reservation == null)
                return null;

            var dto = MapToDto(reservation);
            if (!canViewAll)
                dto.CreatedByUserId = null;

            return dto;
        }

        public async Task<CursorPageResult<ReservationResponseDto>> SearchAsync(
            ReservationQueryParameters queryParams,
            int? currentUserId,
            bool canViewAll)
        {
            if (!canViewAll && !currentUserId.HasValue)
                throw new UnauthorizedAccessException("Access required.");

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

            if (!canViewAll && currentUserId.HasValue)
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
            if (!canViewAll)
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

            await EnsurePendingLimitAsync(createdByUserId, null);
            await _availabilityService.ValidateReservationAsync(dto.RoomId, startUtc, endUtc);

            var reservation = new Reservation
            {
                RoomId = dto.RoomId,
                StartTimeUtc = startUtc,
                EndTimeUtc = endUtc,
                Purpose = dto.Purpose,
                Status = ReservationStatus.Pending,
                CreatedByUserId = createdByUserId,
                GuestEmail = null,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = GetPendingExpiry(startUtc)
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

        public async Task<ReservationResponseDto> CreateGuestAsync(
            ReservationCreateDto dto,
            string guestEmail)
        {
            if (string.IsNullOrWhiteSpace(guestEmail))
                throw new InvalidOperationException("Guest email is required.");

            var startUtc = dto.StartTimeUtc.ToUniversalTime();
            var endUtc = dto.EndTimeUtc.ToUniversalTime();
            var normalizedEmail = NormalizeEmail(guestEmail);

            await EnsurePendingLimitAsync(null, normalizedEmail);
            await _availabilityService.ValidateReservationAsync(dto.RoomId, startUtc, endUtc);

            var reservation = new Reservation
            {
                RoomId = dto.RoomId,
                StartTimeUtc = startUtc,
                EndTimeUtc = endUtc,
                Purpose = dto.Purpose,
                Status = ReservationStatus.Pending,
                CreatedByUserId = null,
                GuestEmail = normalizedEmail,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = GetPendingExpiry(startUtc)
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
            bool canManage)
        {
            var reservation = await _context.Reservations.FirstOrDefaultAsync(r =>
                r.Id == id && (canManage || r.CreatedByUserId == currentUserId));

            if (reservation == null)
                throw new KeyNotFoundException("Reservation not found.");

            var hasNewRoom = dto.RoomId != default && dto.RoomId != reservation.RoomId;
            var targetRoomId = hasNewRoom ? dto.RoomId : reservation.RoomId;
            var startUtc = dto.StartTimeUtc.ToUniversalTime();
            var endUtc = dto.EndTimeUtc.ToUniversalTime();

            var roomExists = await _context.Rooms.AnyAsync(r => r.Id == targetRoomId);
            if (!roomExists)
                throw new InvalidOperationException("Room does not exist.");

            await _availabilityService.ValidateReservationAsync(
                targetRoomId,
                startUtc,
                endUtc,
                reservation.Id);

            reservation.RoomId = targetRoomId;
            reservation.StartTimeUtc = startUtc;
            reservation.EndTimeUtc = endUtc;
            reservation.Purpose = dto.Purpose;
            if (reservation.Status == ReservationStatus.Pending)
            {
                reservation.ExpiresAtUtc = GetPendingExpiry(startUtc);
            }

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
            bool canManage)
        {
            var reservation = await _context.Reservations.FirstOrDefaultAsync(r =>
                r.Id == id && (canManage || r.CreatedByUserId == currentUserId));

            if (reservation == null)
                return false;

            reservation.IsDeleted = true;
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<ReservationResponseDto> ApproveAsync(int id, int approverUserId)
        {
            var reservation = await _context.Reservations
                .FirstOrDefaultAsync(r => r.Id == id);

            if (reservation == null)
                throw new KeyNotFoundException("Reservation not found.");

            if (reservation.Status == ReservationStatus.Pending &&
                reservation.ExpiresAtUtc.HasValue &&
                reservation.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException("Reservation has expired.");
            }

            if (reservation.Status == ReservationStatus.Approved)
                return MapToDto(reservation);

            if (reservation.Status != ReservationStatus.Pending)
                throw new InvalidOperationException("Only pending reservations can be approved.");

            var hasConflict = await _context.Reservations
                .AsNoTracking()
                .AnyAsync(r =>
                    r.Id != reservation.Id &&
                    r.RoomId == reservation.RoomId &&
                    r.Status == ReservationStatus.Approved &&
                    r.StartTimeUtc < reservation.EndTimeUtc &&
                    r.EndTimeUtc > reservation.StartTimeUtc);
            if (hasConflict)
                throw new InvalidOperationException("The room is already reserved in the given time range.");

            reservation.Status = ReservationStatus.Approved;
            reservation.ExpiresAtUtc = null;
            await _context.SaveChangesAsync();

            AddAuditLog("ReservationApproved", "Reservation", reservation.Id.ToString(), approverUserId, reservation.Purpose);
            await _context.SaveChangesAsync();

            return MapToDto(reservation);
        }

        public async Task<ReservationResponseDto> RejectAsync(int id, int approverUserId)
        {
            var reservation = await _context.Reservations
                .FirstOrDefaultAsync(r => r.Id == id);

            if (reservation == null)
                throw new KeyNotFoundException("Reservation not found.");

            if (reservation.Status == ReservationStatus.Rejected)
                return MapToDto(reservation);

            if (reservation.Status != ReservationStatus.Pending)
                throw new InvalidOperationException("Only pending reservations can be rejected.");

            reservation.Status = ReservationStatus.Rejected;
            reservation.ExpiresAtUtc = null;
            await _context.SaveChangesAsync();

            AddAuditLog("ReservationRejected", "Reservation", reservation.Id.ToString(), approverUserId, reservation.Purpose);
            await _context.SaveChangesAsync();

            return MapToDto(reservation);
        }

        private void AddAuditLog(string action, string entityType, string entityId, int userId, string? summary)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Summary = summary,
                CreatedByUserId = userId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        private static int ClampPageSize(int pageSize)
        {
            if (pageSize < 1) return 10;
            if (pageSize > 100) return 100;
            return pageSize;
        }

        private DateTimeOffset? GetPendingExpiry(DateTimeOffset startUtc)
        {
            return startUtc;
        }

        private async Task EnsurePendingLimitAsync(int? userId, string? guestEmail)
        {
            if (_policy.MaxPendingPerUser <= 0)
                return;

            var now = DateTimeOffset.UtcNow;
            var query = _context.Reservations
                .AsNoTracking()
                .Where(r => r.Status == ReservationStatus.Pending)
                .Where(r => r.ExpiresAtUtc == null || r.ExpiresAtUtc > now);

            if (userId.HasValue)
            {
                query = query.Where(r => r.CreatedByUserId == userId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(guestEmail))
            {
                query = query.Where(r => r.GuestEmail == guestEmail);
            }
            else
            {
                return;
            }

            var count = await query.CountAsync();
            if (count >= _policy.MaxPendingPerUser)
                throw new InvalidOperationException("Pending reservation limit reached.");
        }

        private static string NormalizeEmail(string email) =>
            email.Trim().ToLowerInvariant();

        private static ReservationResponseDto MapToDto(Reservation reservation)
        {
            var now = DateTimeOffset.UtcNow;
            var status = reservation.Status.ToString();
            if (reservation.Status == ReservationStatus.Pending &&
                reservation.ExpiresAtUtc.HasValue &&
                reservation.ExpiresAtUtc.Value <= now)
            {
                status = "Expired";
            }

            return new ReservationResponseDto
            {
                Id = reservation.Id,
                RoomId = reservation.RoomId,
                Start = reservation.StartTimeUtc,
                End = reservation.EndTimeUtc,
                Purpose = reservation.Purpose,
                Status = status,
                CreatedByUserId = reservation.CreatedByUserId,
                CreatedAtUtc = reservation.CreatedAtUtc
            };
        }

        private record ReservationCursor(DateTimeOffset StartUtc, int Id, bool IsDescending);
    }
}
