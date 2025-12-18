using eduHub.Application.Common;
using eduHub.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace eduHub.Application.Interfaces.Rooms
{
    public interface IRoomService
    {
        Task<List<Room>> GetByBuildingAsync(int buildingId);
        Task<Room?> GetByIdAsync(int id);
        Task<Room> CreateAsync(Room room);
        Task<Room> UpdateAsync(Room room);
        Task<bool> DeleteAsync(int id);
        Task<List<Room>> GetAvailableRoomsAsync(int buildingId, DateTimeOffset startTimeUtc, DateTimeOffset endTimeUtc);
        Task<CursorPageResult<Room>> GetByBuildingIdPagedAsync(int buildingId, int pageSize, string? cursor);
    }
}
