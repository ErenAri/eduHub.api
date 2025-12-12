using eduHub.Application.Common;
using eduHub.Application.Interfaces.Buildings;
using eduHub.Domain.Entities;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using eduHub.Application.Common.Exceptions;


namespace eduHub.Infrastructure.Services;

public class BuildingService : IBuildingService
{
    private readonly AppDbContext _context;

    public BuildingService(AppDbContext context)
    {
        _context = context;
    }
    public async Task<List<Building>> GetAllAsync()
    {
        return await _context.Buildings
            .Include(b => b.Rooms)
            .ToListAsync();
    }

    public async Task<Building?> GetByIdAsync(int id)
    {
        return await _context.Buildings
            .Include(b => b.Rooms)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<Building> CreateAsync(Building building)
    {
        _context.Buildings.Add(building);
        await _context.SaveChangesAsync();
        return building;
    }

    public async Task<Building> UpdateAsync(Building building)
    {
        _context.Buildings.Update(building);
        await _context.SaveChangesAsync();
        return building;
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _context.Buildings.FirstOrDefaultAsync(b => b.Id == id);

        if (entity == null)
            throw new KeyNotFoundException("Building not found.");

        var hasRooms = await _context.Rooms.AnyAsync(r => r.BuildingId == id);
        if (hasRooms)
            throw new ConflictException(
                "Cannot delete building because it has rooms. Delete or move rooms first."
            );

        _context.Buildings.Remove(entity);
        await _context.SaveChangesAsync();
    }
    public async Task<PagedResult<Building>> GetPagedAsync(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;

        var query = _context.Buildings
            .AsNoTracking()
            .OrderBy(b => b.Name);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Building>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
