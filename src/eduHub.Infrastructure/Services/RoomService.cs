using eduHub.Application.Common;
using eduHub.Application.Interfaces.Rooms;
using eduHub.Domain.Entities;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
        var entity = await _context.Rooms.FindAsync(id);
        if (entity == null) return false;

        _context.Rooms.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<Room>> GetAvailableRoomsAsync(
      int buildingId,
      DateTime startTimeUtc,
      DateTime endTimeUtc)
    {
        return await _context.Rooms
            .Where(r => r.BuildingId == buildingId)
            .Where(r => !r.Reservations.Any(res =>
                res.StartTimeUtc < endTimeUtc &&
                res.EndTimeUtc > startTimeUtc))
            .AsNoTracking()
            .ToListAsync();
    }
    public async Task<PagedResult<Room>> GetByBuildingIdPagedAsync(int buildingId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var query = _context.Rooms
            .AsNoTracking()
            .Where(r => r.BuildingId == buildingId)
            .OrderBy(r => r.Name);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Room>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
