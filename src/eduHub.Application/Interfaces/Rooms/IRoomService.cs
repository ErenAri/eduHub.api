using eduHub.Application.Common;
using eduHub.Domain.Entities;

namespace eduHub.Application.Interfaces.Rooms;

public interface IRoomService
{
    Task<List<Room>> GetByBuildingAsync(int buildingId);
    Task<Room?> GetByIdAsync(int id);
    Task<Room> CreateAsync(Room room, int createdByUserId);
    Task<Room> UpdateAsync(Room room, int updatedByUserId);
    Task<bool> DeleteAsync(int id, int deletedByUserId);
    Task<List<Room>> GetAvailableRoomsAsync(int buildingId, DateTimeOffset startTimeUtc, DateTimeOffset endTimeUtc);
    Task<CursorPageResult<Room>> GetByBuildingIdPagedAsync(int buildingId, int pageSize, string? cursor);
}
