using eduHub.Application.Common;
using eduHub.Application.Interfaces.Buildings;
using System;
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
    public async Task<CursorPageResult<Building>> GetPagedAsync(int pageSize, string? cursor)
    {
        pageSize = ClampPageSize(pageSize);

        var query = _context.Buildings
            .AsNoTracking()
            .AsQueryable();

        if (CursorSerializer.TryDecode<BuildingCursor>(cursor, out var parsed))
        {
            query = query.Where(b =>
                string.Compare(b.Name, parsed!.Name) > 0 ||
                (b.Name == parsed.Name && b.Id > parsed.Id));
        }

        query = query
            .OrderBy(b => b.Name)
            .ThenBy(b => b.Id);

        var items = await query
            .Take(pageSize + 1)
            .ToListAsync();

        var hasMore = items.Count > pageSize;
        if (hasMore)
            items = items.Take(pageSize).ToList();

        var nextCursor = hasMore
            ? CursorSerializer.Encode(new BuildingCursor(items.Last().Name, items.Last().Id))
            : null;

        return new CursorPageResult<Building>
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

    private record BuildingCursor(string Name, int Id);
}
