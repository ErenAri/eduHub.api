using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using eduHub.Application.DTOs.Reservations;
using eduHub.Application.DTOs.Users;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using eduHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace eduHub.IntegrationTests;

public class ServiceIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("eduhub_integration")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task RefreshTokenReuse_RevokesAllTokensForUser()
    {
        await using var context = await CreateDbContextAsync();
        var config = BuildConfiguration();
        var userService = new UserService(context, config);

        var password = "StrongPassword123!";
        var registered = await userService.RegisterAsync(new UserRegisterDto
        {
            UserName = $"user-{Guid.NewGuid():N}",
            Email = $"user-{Guid.NewGuid():N}@example.com",
            Password = password
        });

        var auth = await userService.LoginAsync(new UserLoginDto
        {
            UserNameOrEmail = registered.UserName,
            Password = password
        });

        Assert.NotNull(auth);

        var refreshed = await userService.RefreshAsync(new RefreshRequestDto
        {
            RefreshToken = auth!.RefreshToken
        });

        Assert.NotNull(refreshed);

        var reused = await userService.RefreshAsync(new RefreshRequestDto
        {
            RefreshToken = auth.RefreshToken
        });

        Assert.Null(reused);

        var tokens = await context.RefreshTokens
            .Where(rt => rt.UserId == registered.Id)
            .ToListAsync();

        Assert.NotEmpty(tokens);
        Assert.All(tokens, token => Assert.NotNull(token.RevokedAtUtc));
    }

    [Fact]
    public async Task LogoutRevokesRefreshTokensForUser()
    {
        await using var context = await CreateDbContextAsync();
        var config = BuildConfiguration();
        var userService = new UserService(context, config);

        var password = "StrongPassword123!";
        var registered = await userService.RegisterAsync(new UserRegisterDto
        {
            UserName = $"user-{Guid.NewGuid():N}",
            Email = $"user-{Guid.NewGuid():N}@example.com",
            Password = password
        });

        var auth = await userService.LoginAsync(new UserLoginDto
        {
            UserNameOrEmail = registered.UserName,
            Password = password
        });

        Assert.NotNull(auth);

        await userService.RevokeRefreshTokensAsync(registered.Id);

        var tokens = await context.RefreshTokens
            .Where(rt => rt.UserId == registered.Id)
            .ToListAsync();

        Assert.NotEmpty(tokens);
        Assert.All(tokens, token => Assert.NotNull(token.RevokedAtUtc));
    }

    [Fact]
    public async Task CreateReservation_MissingRoom_Throws()
    {
        await using var context = await CreateDbContextAsync();
        var user = CreateUser("missing-room");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var dto = new ReservationCreateDto
        {
            RoomId = 1000,
            StartTimeUtc = DateTimeOffset.UtcNow.AddHours(1),
            EndTimeUtc = DateTimeOffset.UtcNow.AddHours(2),
            Purpose = "Test"
        };

        var service = new ReservationService(context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(dto, user.Id));
        Assert.Contains("Room does not exist", ex.Message);
    }

    [Fact]
    public async Task CreateReservation_DeletedRoom_Throws()
    {
        await using var context = await CreateDbContextAsync();
        var user = CreateUser("deleted-room");
        context.Users.Add(user);

        var building = new Building { Name = "Deleted Building" };
        context.Buildings.Add(building);
        await context.SaveChangesAsync();

        var room = new Room
        {
            Name = "Room 1",
            Code = "R1",
            Capacity = 10,
            BuildingId = building.Id
        };

        context.Rooms.Add(room);
        await context.SaveChangesAsync();

        room.IsDeleted = true;
        await context.SaveChangesAsync();

        var dto = new ReservationCreateDto
        {
            RoomId = room.Id,
            StartTimeUtc = DateTimeOffset.UtcNow.AddHours(3),
            EndTimeUtc = DateTimeOffset.UtcNow.AddHours(4),
            Purpose = "Test"
        };

        var service = new ReservationService(context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(dto, user.Id));
        Assert.Contains("Room does not exist", ex.Message);
    }

    [Fact]
    public async Task GetAvailableRooms_IgnoresRejectedReservations()
    {
        await using var context = await CreateDbContextAsync();
        var building = new Building { Name = "Availability Building" };
        context.Buildings.Add(building);
        await context.SaveChangesAsync();

        var room = new Room
        {
            Name = "Room 1",
            Code = "R1",
            Capacity = 10,
            BuildingId = building.Id
        };

        context.Rooms.Add(room);
        await context.SaveChangesAsync();

        var start = DateTimeOffset.UtcNow.AddDays(1);
        var end = start.AddHours(1);
        context.Reservations.Add(new Reservation
        {
            RoomId = room.Id,
            StartTimeUtc = start,
            EndTimeUtc = end,
            Purpose = "Rejected",
            Status = ReservationStatus.Rejected,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync();

        var service = new RoomService(context);
        var available = await service.GetAvailableRoomsAsync(building.Id, start, end);

        Assert.Contains(available, r => r.Id == room.Id);
    }

    [Fact]
    public async Task BuildingCursor_ReturnsNextPage()
    {
        await using var context = await CreateDbContextAsync();
        var service = new BuildingService(context);

        var prefix = $"cursor-{Guid.NewGuid():N}";
        var first = new Building { Name = $"{prefix}-a" };
        var second = new Building { Name = $"{prefix}-b" };
        var third = new Building { Name = $"{prefix}-c" };

        context.Buildings.AddRange(first, second, third);
        await context.SaveChangesAsync();

        var firstPage = await service.GetPagedAsync(1, null);
        Assert.Single(firstPage.Items);
        Assert.Equal($"{prefix}-a", firstPage.Items[0].Name);
        Assert.True(firstPage.HasMore);
        Assert.NotNull(firstPage.NextCursor);

        var secondPage = await service.GetPagedAsync(1, firstPage.NextCursor);
        Assert.Single(secondPage.Items);
        Assert.Equal($"{prefix}-b", secondPage.Items[0].Name);
    }

    private async Task<AppDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var context = new AppDbContext(options);
        await context.Database.MigrateAsync();
        await ResetDatabaseAsync(context);
        return context;
    }

    private static async Task ResetDatabaseAsync(AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE buildings, rooms, reservations, users, refresh_tokens, revoked_tokens RESTART IDENTITY CASCADE;");
    }

    private static IConfiguration BuildConfiguration()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "test-key-test-key-test-key-test-key-1234",
            ["Jwt:Issuer"] = "eduHub",
            ["Jwt:Audience"] = "eduHub",
            ["Jwt:AccessTokenMinutes"] = "15",
            ["Jwt:RefreshTokenDays"] = "7"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private static User CreateUser(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new User
        {
            UserName = $"{prefix}-{suffix}",
            Email = $"{prefix}-{suffix}@example.com",
            PasswordHash = "hash",
            Role = UserRole.User,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
