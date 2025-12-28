using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using eduHub.Application.DTOs.Availability;
using eduHub.Application.Interfaces.Availability;
using eduHub.Application.Interfaces.Tenants;
using eduHub.Application.Options;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace eduHub.Infrastructure.Services;

public class AvailabilityService : IAvailabilityService
{
    private const int MinutesPerDay = 1440;
    private readonly AppDbContext _context;
    private readonly ICurrentTenant _tenant;
    private readonly ReservationPolicyOptions _policy;

    public AvailabilityService(
        AppDbContext context,
        ICurrentTenant tenant,
        IOptions<ReservationPolicyOptions> policyOptions)
    {
        _context = context;
        _tenant = tenant;
        _policy = policyOptions.Value;
    }

    public async Task<IReadOnlyList<AvailabilityWindowDto>> GetBuildingHoursAsync(
        int buildingId)
    {
        await EnsureBuildingAsync(buildingId);

        var windows = await _context.BuildingAvailabilityWindows
            .AsNoTracking()
            .Where(w => w.BuildingId == buildingId)
            .ToListAsync();

        if (windows.Count == 0)
            return DefaultAllDayWindows();

        return windows
            .Select(MapWindow)
            .ToList();
    }

    public async Task<IReadOnlyList<AvailabilityWindowDto>> GetRoomHoursAsync(
        int roomId)
    {
        var context = await GetRoomContextAsync(roomId);

        var windows = await _context.RoomAvailabilityWindows
            .AsNoTracking()
            .Where(w => w.RoomId == roomId)
            .ToListAsync();

        if (windows.Count > 0)
        {
            return windows
                .Select(MapWindow)
                .ToList();
        }

        var buildingWindows = await _context.BuildingAvailabilityWindows
            .AsNoTracking()
            .Where(w => w.BuildingId == context.Building.Id)
            .ToListAsync();

        if (buildingWindows.Count > 0)
        {
            return buildingWindows
                .Select(MapWindow)
                .ToList();
        }

        return DefaultAllDayWindows();
    }

    public async Task<IReadOnlyList<AvailabilityWindowDto>> UpdateBuildingHoursAsync(
        int buildingId,
        IReadOnlyList<AvailabilityWindowDto> windows,
        int updatedByUserId)
    {
        var building = await EnsureBuildingAsync(buildingId);
        ValidateWindows(windows);

        var existing = await _context.BuildingAvailabilityWindows
            .Where(w => w.BuildingId == buildingId)
            .ToListAsync();

        _context.BuildingAvailabilityWindows.RemoveRange(existing);

        foreach (var window in windows)
        {
            _context.BuildingAvailabilityWindows.Add(new BuildingAvailabilityWindow
            {
                BuildingId = buildingId,
                DayOfWeek = (DayOfWeek)window.DayOfWeek,
                StartMinute = window.StartMinute,
                EndMinute = window.EndMinute
            });
        }

        await _context.SaveChangesAsync();

        AddAuditLog(
            "BuildingHoursUpdated",
            "Building",
            buildingId.ToString(),
            updatedByUserId,
            building.Name);

        await _context.SaveChangesAsync();

        return await GetBuildingHoursAsync(buildingId);
    }

    public async Task<IReadOnlyList<AvailabilityWindowDto>> UpdateRoomHoursAsync(
        int roomId,
        IReadOnlyList<AvailabilityWindowDto> windows,
        int updatedByUserId)
    {
        var room = await EnsureRoomAsync(roomId);
        ValidateWindows(windows);

        var existing = await _context.RoomAvailabilityWindows
            .Where(w => w.RoomId == roomId)
            .ToListAsync();

        _context.RoomAvailabilityWindows.RemoveRange(existing);

        foreach (var window in windows)
        {
            _context.RoomAvailabilityWindows.Add(new RoomAvailabilityWindow
            {
                RoomId = roomId,
                DayOfWeek = (DayOfWeek)window.DayOfWeek,
                StartMinute = window.StartMinute,
                EndMinute = window.EndMinute
            });
        }

        await _context.SaveChangesAsync();

        AddAuditLog(
            "RoomHoursUpdated",
            "Room",
            roomId.ToString(),
            updatedByUserId,
            room.Name);

        await _context.SaveChangesAsync();

        return await GetRoomHoursAsync(roomId);
    }

    public async Task<IReadOnlyList<AvailabilityBlackoutResponseDto>> GetBlackoutsAsync(
        int? buildingId,
        int? roomId,
        DateTimeOffset? startTimeUtc,
        DateTimeOffset? endTimeUtc)
    {
        var query = _context.AvailabilityBlackouts.AsNoTracking();

        if (roomId.HasValue)
        {
            query = query.Where(b => b.RoomId == roomId.Value);
        }
        else if (buildingId.HasValue)
        {
            query = query.Where(b => b.BuildingId == buildingId.Value && b.RoomId == null);
        }

        if (startTimeUtc.HasValue)
            query = query.Where(b => b.EndTimeUtc >= startTimeUtc.Value);

        if (endTimeUtc.HasValue)
            query = query.Where(b => b.StartTimeUtc <= endTimeUtc.Value);

        var items = await query
            .OrderBy(b => b.StartTimeUtc)
            .ToListAsync();

        return items.Select(MapBlackout).ToList();
    }

    public async Task<AvailabilityBlackoutResponseDto> CreateBlackoutAsync(
        AvailabilityBlackoutCreateDto dto,
        int createdByUserId)
    {
        if (dto.StartTimeUtc >= dto.EndTimeUtc)
            throw new InvalidOperationException("Blackout end must be after start.");

        if (dto.BuildingId.HasValue && dto.RoomId.HasValue)
            throw new InvalidOperationException("Blackout cannot target a building and room at the same time.");

        if (!string.IsNullOrWhiteSpace(dto.Reason) && dto.Reason.Trim().Length > 500)
            throw new InvalidOperationException("Reason must be 500 characters or less.");

        Building? building = null;
        Room? room = null;

        if (dto.RoomId.HasValue)
        {
            room = await _context.Rooms
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == dto.RoomId.Value);
            if (room == null)
                throw new KeyNotFoundException("Room not found.");
        }
        else if (dto.BuildingId.HasValue)
        {
            building = await _context.Buildings
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == dto.BuildingId.Value);
            if (building == null)
                throw new KeyNotFoundException("Building not found.");
        }

        var blackout = new AvailabilityBlackout
        {
            BuildingId = dto.BuildingId,
            RoomId = dto.RoomId,
            StartTimeUtc = dto.StartTimeUtc.ToUniversalTime(),
            EndTimeUtc = dto.EndTimeUtc.ToUniversalTime(),
            Reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim(),
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _context.AvailabilityBlackouts.Add(blackout);
        await _context.SaveChangesAsync();

        AddAuditLog(
            "AvailabilityBlackoutCreated",
            "AvailabilityBlackout",
            blackout.Id.ToString(),
            createdByUserId,
            blackout.Reason);

        await _context.SaveChangesAsync();

        return MapBlackout(blackout);
    }

    public async Task<bool> DeleteBlackoutAsync(int id, int deletedByUserId)
    {
        var blackout = await _context.AvailabilityBlackouts
            .FirstOrDefaultAsync(b => b.Id == id);
        if (blackout == null)
            return false;

        _context.AvailabilityBlackouts.Remove(blackout);
        await _context.SaveChangesAsync();

        AddAuditLog(
            "AvailabilityBlackoutDeleted",
            "AvailabilityBlackout",
            id.ToString(),
            deletedByUserId,
            blackout.Reason);

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyList<AvailabilitySlotDto>> GetRoomAvailabilityAsync(
        int roomId,
        DateTimeOffset startTimeUtc,
        DateTimeOffset endTimeUtc)
    {
        if (startTimeUtc >= endTimeUtc)
            throw new InvalidOperationException("End time must be after start time.");

        var now = DateTimeOffset.UtcNow;
        var earliest = now.AddMinutes(_policy.LeadTimeMinutes);
        var latest = now.AddDays(_policy.MaxAdvanceDays);

        var rangeStart = startTimeUtc < earliest ? earliest : startTimeUtc;
        var rangeEnd = endTimeUtc > latest ? latest : endTimeUtc;
        if (rangeStart >= rangeEnd)
            return Array.Empty<AvailabilitySlotDto>();

        var context = await GetRoomContextAsync(roomId);
        var timeZone = await GetOrganizationTimeZoneAsync();
        var windows = await GetEffectiveWindowsAsync(roomId, context.Building.Id);
        var openWindows = BuildOpenWindows(rangeStart, rangeEnd, timeZone, windows);
        if (openWindows.Count == 0)
            return Array.Empty<AvailabilitySlotDto>();

        var blocks = await GetBlockingWindowsAsync(
            context.Building.Id,
            roomId,
            rangeStart,
            rangeEnd,
            includeBuffer: true,
            excludeReservationId: null);

        var available = SubtractWindows(openWindows, blocks);

        return available
            .Select(w => new AvailabilitySlotDto
            {
                StartTimeUtc = w.Start,
                EndTimeUtc = w.End
            })
            .ToList();
    }

    public async Task ValidateReservationAsync(
        int roomId,
        DateTimeOffset startTimeUtc,
        DateTimeOffset endTimeUtc,
        int? excludeReservationId = null)
    {
        if (startTimeUtc >= endTimeUtc)
            throw new InvalidOperationException("End time must be after start time.");

        var durationMinutes = (endTimeUtc - startTimeUtc).TotalMinutes;
        if (durationMinutes > _policy.MaxDurationMinutes)
            throw new InvalidOperationException("Reservation exceeds the maximum duration.");

        var now = DateTimeOffset.UtcNow;
        if (startTimeUtc < now.AddMinutes(_policy.LeadTimeMinutes))
            throw new InvalidOperationException("Reservation must be made in advance.");

        if (startTimeUtc > now.AddDays(_policy.MaxAdvanceDays))
            throw new InvalidOperationException("Reservation is too far in the future.");

        var context = await GetRoomContextAsync(roomId);
        var timeZone = await GetOrganizationTimeZoneAsync();
        await EnsureWithinHoursAsync(roomId, context.Building.Id, startTimeUtc, endTimeUtc, timeZone);
        await EnsureNoBlackoutsAsync(context.Building.Id, roomId, startTimeUtc, endTimeUtc);
        await EnsureNoReservationConflictsAsync(roomId, startTimeUtc, endTimeUtc, excludeReservationId);
    }

    public async Task<bool> IsRoomAvailableAsync(
        int roomId,
        DateTimeOffset startTimeUtc,
        DateTimeOffset endTimeUtc,
        int? excludeReservationId = null)
    {
        try
        {
            await ValidateReservationAsync(roomId, startTimeUtc, endTimeUtc, excludeReservationId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<Building> EnsureBuildingAsync(int buildingId)
    {
        var building = await _context.Buildings.FirstOrDefaultAsync(b => b.Id == buildingId);
        if (building == null)
            throw new KeyNotFoundException("Building not found.");
        return building;
    }

    private async Task<Room> EnsureRoomAsync(int roomId)
    {
        var room = await _context.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room == null)
            throw new KeyNotFoundException("Room not found.");
        return room;
    }

    private async Task<(Room Room, Building Building)> GetRoomContextAsync(int roomId)
    {
        var room = await _context.Rooms
            .Include(r => r.Building)
            .FirstOrDefaultAsync(r => r.Id == roomId);

        if (room == null)
            throw new KeyNotFoundException("Room not found.");

        if (room.Building == null)
            throw new InvalidOperationException("Building not found.");

        return (room, room.Building);
    }

    private async Task<IReadOnlyList<AvailabilityWindowDto>> GetEffectiveWindowsAsync(
        int roomId,
        int buildingId)
    {
        var roomWindows = await _context.RoomAvailabilityWindows
            .AsNoTracking()
            .Where(w => w.RoomId == roomId)
            .ToListAsync();

        if (roomWindows.Count > 0)
            return roomWindows.Select(MapWindow).ToList();

        var buildingWindows = await _context.BuildingAvailabilityWindows
            .AsNoTracking()
            .Where(w => w.BuildingId == buildingId)
            .ToListAsync();

        if (buildingWindows.Count > 0)
            return buildingWindows.Select(MapWindow).ToList();

        return DefaultAllDayWindows();
    }

    private async Task EnsureWithinHoursAsync(
        int roomId,
        int buildingId,
        DateTimeOffset startTimeUtc,
        DateTimeOffset endTimeUtc,
        TimeZoneInfo timeZone)
    {
        var windows = await GetEffectiveWindowsAsync(roomId, buildingId);
        var startLocal = TimeZoneInfo.ConvertTime(startTimeUtc, timeZone);
        var endLocal = TimeZoneInfo.ConvertTime(endTimeUtc, timeZone);

        var startDate = startLocal.Date;
        var endDate = endLocal.Date;
        var endMinute = endLocal.Hour * 60 + endLocal.Minute;

        if (endDate > startDate)
        {
            if (endLocal.TimeOfDay != TimeSpan.Zero || endDate > startDate.AddDays(1))
                throw new InvalidOperationException("Reservation must stay within a single day.");

            endMinute = MinutesPerDay;
        }

        var startMinute = startLocal.Hour * 60 + startLocal.Minute;
        var dayOfWeek = (int)startDate.DayOfWeek;

        var dayWindows = windows.Where(w => w.DayOfWeek == dayOfWeek).ToList();
        if (dayWindows.Count == 0)
            throw new InvalidOperationException("Selected time is outside of building hours.");

        var fitsWindow = dayWindows.Any(w =>
            w.StartMinute <= startMinute && endMinute <= w.EndMinute);

        if (!fitsWindow)
            throw new InvalidOperationException("Selected time is outside of building hours.");
    }

    private async Task EnsureNoBlackoutsAsync(
        int buildingId,
        int roomId,
        DateTimeOffset startTimeUtc,
        DateTimeOffset endTimeUtc)
    {
        var hasBlackout = await _context.AvailabilityBlackouts
            .AsNoTracking()
            .AnyAsync(b =>
                (b.RoomId == roomId ||
                 (b.RoomId == null && b.BuildingId == buildingId) ||
                 (b.RoomId == null && b.BuildingId == null)) &&
                b.StartTimeUtc < endTimeUtc &&
                b.EndTimeUtc > startTimeUtc);

        if (hasBlackout)
            throw new InvalidOperationException("Selected time is blocked.");
    }

    private async Task EnsureNoReservationConflictsAsync(
        int roomId,
        DateTimeOffset startTimeUtc,
        DateTimeOffset endTimeUtc,
        int? excludeReservationId)
    {
        var now = DateTimeOffset.UtcNow;
        var bufferStart = startTimeUtc.AddMinutes(-_policy.BufferMinutes);
        var bufferEnd = endTimeUtc.AddMinutes(_policy.BufferMinutes);

        var query = _context.Reservations
            .AsNoTracking()
            .Where(r => r.RoomId == roomId)
            .Where(r => r.StartTimeUtc < bufferEnd && r.EndTimeUtc > bufferStart)
            .Where(r =>
                r.Status == ReservationStatus.Approved ||
                (r.Status == ReservationStatus.Pending &&
                 (r.ExpiresAtUtc == null || r.ExpiresAtUtc > now)));

        if (excludeReservationId.HasValue)
            query = query.Where(r => r.Id != excludeReservationId.Value);

        if (await query.AnyAsync())
            throw new InvalidOperationException("The room is already reserved in the given time range.");
    }

    private async Task<TimeZoneInfo> GetOrganizationTimeZoneAsync()
    {
        if (!_tenant.OrganizationId.HasValue)
            return TimeZoneInfo.Utc;

        var orgId = _tenant.OrganizationId.Value;
        var tzId = await _context.Organizations
            .AsNoTracking()
            .Where(o => o.Id == orgId)
            .Select(o => o.Timezone)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(tzId))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tzId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static void ValidateWindows(IReadOnlyList<AvailabilityWindowDto> windows)
    {
        if (windows.Count == 0)
            return;

        foreach (var window in windows)
        {
            if (window.DayOfWeek < 0 || window.DayOfWeek > 6)
                throw new InvalidOperationException("DayOfWeek must be between 0 and 6.");

            if (window.StartMinute < 0 || window.EndMinute > MinutesPerDay)
                throw new InvalidOperationException("Minutes must be within 0-1440.");

            if (window.StartMinute >= window.EndMinute)
                throw new InvalidOperationException("Start minute must be before end minute.");
        }

        foreach (var group in windows.GroupBy(w => w.DayOfWeek))
        {
            var ordered = group.OrderBy(w => w.StartMinute).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].StartMinute < ordered[i - 1].EndMinute)
                    throw new InvalidOperationException("Availability windows cannot overlap.");
            }
        }
    }

    private static AvailabilityWindowDto MapWindow(BuildingAvailabilityWindow window)
    {
        return new AvailabilityWindowDto
        {
            DayOfWeek = (int)window.DayOfWeek,
            StartMinute = window.StartMinute,
            EndMinute = window.EndMinute
        };
    }

    private static AvailabilityWindowDto MapWindow(RoomAvailabilityWindow window)
    {
        return new AvailabilityWindowDto
        {
            DayOfWeek = (int)window.DayOfWeek,
            StartMinute = window.StartMinute,
            EndMinute = window.EndMinute
        };
    }

    private static IReadOnlyList<AvailabilityWindowDto> DefaultAllDayWindows()
    {
        return Enumerable.Range(0, 7)
            .Select(day => new AvailabilityWindowDto
            {
                DayOfWeek = day,
                StartMinute = 0,
                EndMinute = MinutesPerDay
            })
            .ToList();
    }

    private static AvailabilityBlackoutResponseDto MapBlackout(AvailabilityBlackout blackout)
    {
        return new AvailabilityBlackoutResponseDto
        {
            Id = blackout.Id,
            StartTimeUtc = blackout.StartTimeUtc,
            EndTimeUtc = blackout.EndTimeUtc,
            Reason = blackout.Reason,
            BuildingId = blackout.BuildingId,
            RoomId = blackout.RoomId,
            CreatedByUserId = blackout.CreatedByUserId,
            CreatedAtUtc = blackout.CreatedAtUtc
        };
    }

    private List<TimeRange> BuildOpenWindows(
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        TimeZoneInfo timeZone,
        IReadOnlyList<AvailabilityWindowDto> windows)
    {
        var result = new List<TimeRange>();
        var startLocal = TimeZoneInfo.ConvertTime(rangeStartUtc, timeZone);
        var endLocal = TimeZoneInfo.ConvertTime(rangeEndUtc, timeZone);
        var currentDate = startLocal.Date;
        var lastDate = endLocal.Date;

        var hasExplicitWindows = windows.Count > 0;

        while (currentDate <= lastDate)
        {
            var dayOfWeek = (int)currentDate.DayOfWeek;
            var dayWindows = windows.Where(w => w.DayOfWeek == dayOfWeek).ToList();
            if (!hasExplicitWindows)
            {
                dayWindows = DefaultAllDayWindows().Where(w => w.DayOfWeek == dayOfWeek).ToList();
            }

            foreach (var window in dayWindows)
            {
                var windowStartLocal = currentDate.AddMinutes(window.StartMinute);
                var windowEndLocal = window.EndMinute == MinutesPerDay
                    ? currentDate.AddDays(1)
                    : currentDate.AddMinutes(window.EndMinute);

                var windowStartUtc = ConvertLocalToUtc(windowStartLocal, timeZone);
                var windowEndUtc = ConvertLocalToUtc(windowEndLocal, timeZone);

                var start = Max(windowStartUtc, rangeStartUtc);
                var end = Min(windowEndUtc, rangeEndUtc);
                if (start < end)
                    result.Add(new TimeRange(start, end));
            }

            currentDate = currentDate.AddDays(1);
        }

        return result.OrderBy(r => r.Start).ToList();
    }

    private async Task<List<TimeRange>> GetBlockingWindowsAsync(
        int buildingId,
        int roomId,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        bool includeBuffer,
        int? excludeReservationId)
    {
        var blocks = new List<TimeRange>();

        var blackouts = await _context.AvailabilityBlackouts
            .AsNoTracking()
            .Where(b =>
                (b.RoomId == roomId ||
                 (b.RoomId == null && b.BuildingId == buildingId) ||
                 (b.RoomId == null && b.BuildingId == null)) &&
                b.StartTimeUtc < rangeEndUtc &&
                b.EndTimeUtc > rangeStartUtc)
            .ToListAsync();

        foreach (var blackout in blackouts)
        {
            blocks.Add(new TimeRange(blackout.StartTimeUtc, blackout.EndTimeUtc));
        }

        var now = DateTimeOffset.UtcNow;
        var reservationQuery = _context.Reservations
            .AsNoTracking()
            .Where(r => r.RoomId == roomId)
            .Where(r => r.StartTimeUtc < rangeEndUtc && r.EndTimeUtc > rangeStartUtc)
            .Where(r =>
                r.Status == ReservationStatus.Approved ||
                (r.Status == ReservationStatus.Pending &&
                 (r.ExpiresAtUtc == null || r.ExpiresAtUtc > now)));

        if (excludeReservationId.HasValue)
            reservationQuery = reservationQuery.Where(r => r.Id != excludeReservationId.Value);

        var reservations = await reservationQuery.ToListAsync();
        foreach (var reservation in reservations)
        {
            var start = reservation.StartTimeUtc;
            var end = reservation.EndTimeUtc;
            if (includeBuffer)
            {
                start = start.AddMinutes(-_policy.BufferMinutes);
                end = end.AddMinutes(_policy.BufferMinutes);
            }
            blocks.Add(new TimeRange(start, end));
        }

        return blocks.OrderBy(b => b.Start).ToList();
    }

    private static List<TimeRange> SubtractWindows(
        List<TimeRange> windows,
        List<TimeRange> blocks)
    {
        if (windows.Count == 0)
            return windows;

        if (blocks.Count == 0)
            return windows;

        var result = new List<TimeRange>();
        var orderedBlocks = blocks.OrderBy(b => b.Start).ToList();

        foreach (var window in windows.OrderBy(w => w.Start))
        {
            var cursor = window.Start;
            foreach (var block in orderedBlocks)
            {
                if (block.End <= cursor)
                    continue;

                if (block.Start >= window.End)
                    break;

                var blockStart = block.Start;
                var blockEnd = block.End;

                if (blockStart > cursor)
                {
                    var sliceEnd = Min(blockStart, window.End);
                    if (cursor < sliceEnd)
                        result.Add(new TimeRange(cursor, sliceEnd));
                }

                if (blockEnd > cursor)
                    cursor = Max(blockEnd, cursor);

                if (cursor >= window.End)
                    break;
            }

            if (cursor < window.End)
                result.Add(new TimeRange(cursor, window.End));
        }

        return result;
    }

    private static DateTimeOffset ConvertLocalToUtc(DateTime localDateTime, TimeZoneInfo timeZone)
    {
        if (timeZone.IsInvalidTime(localDateTime))
            localDateTime = localDateTime.AddHours(1);

        var offset = timeZone.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset).ToUniversalTime();
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left > right ? left : right;

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left < right ? left : right;

    private void AddAuditLog(
        string action,
        string entityType,
        string entityId,
        int userId,
        string? summary)
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

    private record TimeRange(DateTimeOffset Start, DateTimeOffset End);
}
