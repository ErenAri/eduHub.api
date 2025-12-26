using eduHub.Application.Interfaces.Organizations;
using eduHub.Domain.Entities;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eduHub.Infrastructure.Services;

public class OrganizationService : IOrganizationService
{
    private readonly AppDbContext _context;

    public OrganizationService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Organization>> GetAllAsync()
    {
        return await _context.Organizations
            .AsNoTracking()
            .OrderBy(o => o.Name)
            .ToListAsync();
    }

    public async Task<Organization?> GetByIdAsync(Guid id)
    {
        return await _context.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Organization?> GetBySlugAsync(string slug)
    {
        return await _context.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Slug == slug);
    }

    public async Task<Organization> CreateAsync(Organization organization)
    {
        _context.Organizations.Add(organization);
        await _context.SaveChangesAsync();
        return organization;
    }

    public async Task<Organization> UpdateStatusAsync(Guid id, bool isActive)
    {
        var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Id == id);
        if (organization == null)
            throw new KeyNotFoundException("Organization not found.");

        organization.IsActive = isActive;
        await _context.SaveChangesAsync();
        return organization;
    }

    public async Task<Organization> UpdatePlanAsync(Guid id, string subscriptionPlan)
    {
        var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Id == id);
        if (organization == null)
            throw new KeyNotFoundException("Organization not found.");

        organization.SubscriptionPlan = subscriptionPlan;
        await _context.SaveChangesAsync();
        return organization;
    }
}
