using eduHub.Domain.Entities;

namespace eduHub.Application.Interfaces.Organizations;

public interface IOrganizationService
{
    Task<List<Organization>> GetAllAsync();
    Task<Organization?> GetByIdAsync(Guid id);
    Task<Organization?> GetBySlugAsync(string slug);
    Task<Organization> CreateAsync(Organization organization);
    Task<Organization> UpdateStatusAsync(Guid id, bool isActive);
    Task<Organization> UpdatePlanAsync(Guid id, string subscriptionPlan);
}
