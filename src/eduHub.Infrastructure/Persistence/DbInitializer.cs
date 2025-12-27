using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using eduHub.Application.Interfaces.Tenants;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace eduHub.Infrastructure.Persistence;

public static class DbInitializer
{
    private const string DefaultOrgName = "Default Organization";
    private const string DefaultOrgSlug = "default";

    public static async Task SeedAsync(
        AppDbContext context,
        IConfiguration configuration,
        IHostEnvironment env,
        ICurrentTenantSetter tenantSetter)
    {
        var seedEnabled = configuration.GetValue("Seed:Enabled", env.IsDevelopment());
        var seedAdmin = configuration.GetValue("Seed:Admin:Enabled", false);
        if (seedAdmin && !env.IsDevelopment())
            throw new InvalidOperationException("Admin seeding is only supported in Development.");

        if (!seedEnabled)
            return;

        var defaultOrg = await EnsureDefaultOrganizationAsync(context);

        if (seedAdmin)
        {
            var adminPassword = configuration["Seed:Admin:Password"];
            await SeedAdminUserAsync(context, defaultOrg.Id, tenantSetter, configuration, adminPassword);
        }

        var seedSampleData = configuration.GetValue("Seed:SampleData:Enabled", env.IsDevelopment());
        if (seedSampleData)
        {
            tenantSetter.SetTenant(defaultOrg.Id);
            try
            {
                await SeedBuildingsAndRoomsAsync(context);
            }
            finally
            {
                tenantSetter.Clear();
            }
        }
    }

    private static async Task<Organization> EnsureDefaultOrganizationAsync(AppDbContext context)
    {
        var org = await context.Organizations.FirstOrDefaultAsync(o => o.Slug == DefaultOrgSlug);
        if (org != null)
            return org;

        org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = DefaultOrgName,
            Slug = DefaultOrgSlug,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org;
    }

    private static async Task SeedAdminUserAsync(
        AppDbContext context,
        Guid organizationId,
        ICurrentTenantSetter tenantSetter,
        IConfiguration configuration,
        string? adminPassword)
    {
        var admin = await context.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin);

        if (admin == null)
        {
            if (string.IsNullOrWhiteSpace(adminPassword))
                throw new InvalidOperationException("Seed:Admin:Password must be set when seeding the admin user.");

            if (adminPassword.Length < 12 ||
                string.Equals(adminPassword, "Admin123!", StringComparison.Ordinal) ||
                string.Equals(adminPassword, "admin", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Seed:Admin:Password does not meet security requirements.");
            }

            var userName = configuration["Seed:Admin:UserName"];
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("Seed:Admin:UserName must be set when seeding the admin user.");

            var email = configuration["Seed:Admin:Email"];
            if (string.IsNullOrWhiteSpace(email))
                throw new InvalidOperationException("Seed:Admin:Email must be set when seeding the admin user.");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);

            admin = new User
            {
                UserName = userName,
                Email = email,
                PasswordHash = passwordHash,
                Role = UserRole.Admin,
                CreatedAtUtc = DateTime.UtcNow
            };

            context.Users.Add(admin);
            await context.SaveChangesAsync();
        }

        tenantSetter.SetTenant(organizationId);
        try
        {
            var membershipExists = await context.OrganizationMembers
                .IgnoreQueryFilters()
                .AnyAsync(m => m.OrganizationId == organizationId && m.UserId == admin.Id);
            if (!membershipExists)
            {
                context.OrganizationMembers.Add(new OrganizationMember
                {
                    OrganizationId = organizationId,
                    UserId = admin.Id,
                    Role = OrganizationMemberRole.OrgAdmin,
                    Status = OrganizationMemberStatus.Active,
                    JoinedAtUtc = DateTimeOffset.UtcNow
                });

                await context.SaveChangesAsync();
            }
        }
        finally
        {
            tenantSetter.Clear();
        }
    }

    private static async Task SeedBuildingsAndRoomsAsync(AppDbContext context)
    {
        if (await context.Buildings.AnyAsync())
            return;

        var main = new Building { Name = "Main Campus" };
        var science = new Building { Name = "Science Block" };

        context.Buildings.AddRange(main, science);
        await context.SaveChangesAsync();

        var rooms = new List<Room>
        {
            new Room { Code = "A101", Name = "Main Lecture Hall", Capacity = 80, BuildingId = main.Id },
            new Room { Code = "A102", Name = "Seminar Room", Capacity = 40, BuildingId = main.Id },
            new Room { Code = "B201", Name = "Lab 1", Capacity = 25, BuildingId = science.Id },
            new Room { Code = "B202", Name = "Lab 2", Capacity = 25, BuildingId = science.Id }
        };

        context.Rooms.AddRange(rooms);
        await context.SaveChangesAsync();
    }
}
