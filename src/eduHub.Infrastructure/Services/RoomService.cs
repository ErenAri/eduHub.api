using eduHub.Application.Common;
using eduHub.Application.Interfaces.Rooms;
using eduHub.Domain.Entities;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;

namespace eduHub.Infrastructure.Services;

public class RoomService : IRoomService
{
    private readonly AppDbContext _context;

    public RoomService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Room>> GetByBuildingAsync(int buildingId)
    {
        return await _context.Rooms
            .Where(r => r.BuildingId == buildingId)
            .Include(r => r.Building)
            .Include(r => r.Reservations)
            .ToListAsync();
    }

    public async Task<Room?> GetByIdAsync(int id)
    {
        return await _context.Rooms
            .Include(r => r.Building)
            .Include(r => r.Reservations)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<Room> CreateAsync(Room room)
    {
        _context.Rooms.Add(room);
        await _context.SaveChangesAsync();
        return room;
    }

    public async Task<Room> UpdateAsync(Room room)
    {
        _context.Rooms.Update(room);
        await _context.SaveChangesAsync();
        return room;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _context.Rooms.FirstOrDefaultAsync(r => r.Id == id);
        if (entity == null) return false;

        entity.IsDeleted = true;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<Room>> GetAvailableRoomsAsync(
      int buildingId,
      DateTimeOffset startTimeUtc,
      DateTimeOffset endTimeUtc)
    {
        var startUtc = startTimeUtc.ToUniversalTime();
        var endUtc = endTimeUtc.ToUniversalTime();

        return await _context.Rooms
            .Where(r => r.BuildingId == buildingId)
            .Where(r => !r.Reservations.Any(res =>
                res.StartTimeUtc < endUtc &&
                res.EndTimeUtc > startUtc))
            .AsNoTracking()
            .ToListAsync();
    }
    public async Task<CursorPageResult<Room>> GetByBuildingIdPagedAsync(int buildingId, int pageSize, string? cursor)
    {
        pageSize = ClampPageSize(pageSize);

        var query = _context.Rooms
            .AsNoTracking()
            .Where(r => r.BuildingId == buildingId)
            .AsQueryable();

        if (CursorSerializer.TryDecode<RoomCursor>(cursor, out var parsed))
        {
            query = query.Where(r =>
                string.Compare(r.Name, parsed!.Name, StringComparison.Ordinal) > 0 ||
                (r.Name == parsed.Name && r.Id > parsed.Id));
        }

        query = query
            .OrderBy(r => r.Name)
            .ThenBy(r => r.Id);

        var items = await query
            .Take(pageSize + 1)
            .ToListAsync();

        var hasMore = items.Count > pageSize;
        if (hasMore)
            items = items.Take(pageSize).ToList();

        var nextCursor = hasMore
            ? CursorSerializer.Encode(new RoomCursor(items.Last().Name, items.Last().Id))
            : null;

        return new CursorPageResult<Room>
        {
            Items = items,
            PageSize = pageSize,
            NextCursor = nextCursor,
            HasMore = hasMore
        };
    }

    private static int ClampPageSize(int pageSize)
    {
        if (pageSize < 1) return 10;
        if (pageSize > 100) return 100;
        return pageSize;
    }

    private record RoomCursor(string Name, int Id);
}
