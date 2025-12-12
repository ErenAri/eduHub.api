using eduHub.Application.Common;
using eduHub.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace eduHub.Application.Interfaces.Buildings
{
    public interface IBuildingService
    {
        Task<List<Building>> GetAllAsync();
        Task<Building?> GetByIdAsync(int id);
        Task<Building> CreateAsync(Building building);
        Task<Building> UpdateAsync(Building building);
        Task DeleteAsync(int id);
        Task<PagedResult<Building>> GetPagedAsync(int page, int pageSize);
    }
}
