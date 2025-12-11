using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace eduHub.Infrastructure.Persistence;

public static class DbInitializer
{
    public static async Task SeedAsync(
        AppDbContext context,
        IConfiguration configuration,
        IHostEnvironment env)
    {
        await context.Database.MigrateAsync();

        var seedAdmin = configuration.GetValue("Seed:Admin:Enabled", false);
        if (seedAdmin)
        {
            var adminPassword = configuration["Seed:Admin:Password"];
            await SeedAdminUserAsync(context, configuration, adminPassword);
        }

        var seedSampleData = configuration.GetValue("Seed:SampleData:Enabled", env.IsDevelopment());
        if (seedSampleData)
        {
            await SeedBuildingsAndRoomsAsync(context);
        }
    }

    private static async Task SeedAdminUserAsync(
        AppDbContext context,
        IConfiguration configuration,
        string? adminPassword)
    {
        if (await context.Users.AnyAsync(u => u.Role == UserRole.Admin))
            return;

        if (string.IsNullOrWhiteSpace(adminPassword))
            throw new InvalidOperationException("Seed:Admin:Password must be set when seeding the admin user.");

        var userName = configuration["Seed:Admin:UserName"] ?? "admin";
        var email = configuration["Seed:Admin:Email"] ?? "admin@eduhub.local";

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);

        var admin = new User
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
