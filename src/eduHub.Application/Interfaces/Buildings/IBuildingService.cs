using eduHub.Application.Common;
using eduHub.Domain.Entities;

namespace eduHub.Application.Interfaces.Buildings;

public interface IBuildingService
{
    Task<List<Building>> GetAllAsync();
    Task<Building?> GetByIdAsync(int id);
    Task<Building> CreateAsync(Building building, int createdByUserId);
    Task<Building> UpdateAsync(Building building, int updatedByUserId);
    Task DeleteAsync(int id, int deletedByUserId);
    Task<CursorPageResult<Building>> GetPagedAsync(int pageSize, string? cursor);
}
