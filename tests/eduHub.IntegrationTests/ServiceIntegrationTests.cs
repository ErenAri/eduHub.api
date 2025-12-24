using System;
using System.Linq;
using System.Threading.Tasks;
using eduHub.Application.DTOs.Reservations;
using eduHub.Application.DTOs.Users;
using eduHub.Application.Security;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using eduHub.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace eduHub.IntegrationTests;

public class ServiceIntegrationTests : IAsyncLifetime
{
    private SqliteConnection _connection;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task RefreshTokenReuse_RevokesAllTokensForUser()
    {
        await using var context = await CreateDbContextAsync();
        var jwtOptions = BuildJwtOptions();
        var userService = new UserService(context, jwtOptions);

        var password = NewPassword();
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
        var jwtOptions = BuildJwtOptions();
        var userService = new UserService(context, jwtOptions);

        var password = NewPassword();
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
            .UseSqlite(_connection)
            .Options;

        var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        // await ResetDatabaseAsync(context); // Not needed for in-memory if we recreate context or connection, but here we share connection
        // Actually, if we reuse the connection, we need to clear data.
        // But here I'm creating a new context for each test invocation?
        // Wait, xUnit instantiates the test class for each test method.
        // So `InitializeAsync` is called for each test.
        // So `_connection` is new for each test.
        // So database is fresh.
        return context;
    }

    private static IOptions<JwtOptions> BuildJwtOptions()
    {
        var options = new JwtOptions
        {
            Key = NewJwtKey(),
            Issuer = "eduHub",
            Audience = "eduHub",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        };

        return Options.Create(options);
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

    private static string NewPassword()
    {
        return $"Pass-{Guid.NewGuid():N}!";
    }

    private static string NewJwtKey()
    {
        return $"{Guid.NewGuid():N}{Guid.NewGuid():N}";
    }
}
