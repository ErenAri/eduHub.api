using eduHub.Application.Common;
using eduHub.Application.Common.Exceptions;
using eduHub.Application.Interfaces.Buildings;
using eduHub.Application.Interfaces.Tenants;
using eduHub.Domain.Entities;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eduHub.Infrastructure.Services;

public class BuildingService : IBuildingService
{
    private readonly AppDbContext _context;
    private readonly ICurrentTenant _tenant;

    public BuildingService(AppDbContext context, ICurrentTenant tenant)
    {
        _context = context;
        _tenant = tenant;
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

    public async Task<Building> CreateAsync(Building building, int createdByUserId)
    {
        _context.Buildings.Add(building);
        await _context.SaveChangesAsync();

        AddAuditLog("BuildingCreated", "Building", building.Id.ToString(), createdByUserId, building.Name);
        await _context.SaveChangesAsync();

        return building;
    }

    public async Task<Building> UpdateAsync(Building building, int updatedByUserId)
    {
        _context.Buildings.Update(building);
        await _context.SaveChangesAsync();

        AddAuditLog("BuildingUpdated", "Building", building.Id.ToString(), updatedByUserId, building.Name);
        await _context.SaveChangesAsync();

        return building;
    }

    public async Task DeleteAsync(int id, int deletedByUserId)
    {
        var entity = await _context.Buildings.FirstOrDefaultAsync(b => b.Id == id);

        if (entity == null)
            throw new KeyNotFoundException("Building not found.");

        var organizationId = _tenant.OrganizationId;
        if (!organizationId.HasValue)
            throw new InvalidOperationException("Tenant context is missing.");

        var hasRooms = await _context.Rooms
            .IgnoreQueryFilters()
            .AnyAsync(r => r.BuildingId == id && r.OrganizationId == organizationId.Value);

        if (hasRooms)
            throw new ConflictException(
                "Cannot delete building because it has rooms. Delete or move rooms first."
            );

        entity.IsDeleted = true;
        await _context.SaveChangesAsync();

        AddAuditLog("BuildingDeleted", "Building", entity.Id.ToString(), deletedByUserId, entity.Name);
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

    private record BuildingCursor(string Name, int Id);
}
