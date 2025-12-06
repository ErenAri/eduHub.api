using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace eduHub.Infrastructure.Persistence;

public static class DbInitializer
{
    public static async Task SeedAsync(AppDbContext context)
    {
        await context.Database.MigrateAsync();

        await SeedAdminUserAsync(context);
        await SeedBuildingsAndRoomsAsync(context);
    }

    private static async Task SeedAdminUserAsync(AppDbContext context)
    {
        if (await context.Users.AnyAsync(u => u.Role == UserRole.Admin))
            return;

        var passwordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!");

        var admin = new User
        {
            UserName = "admin",
            Email = "admin@eduhub.local",
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
