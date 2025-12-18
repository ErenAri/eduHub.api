using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace eduHub.IntegrationTests;

public class ReservationConstraintTests : IAsyncLifetime
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
    public async Task OverlappingReservations_AreBlockedByDatabaseConstraint()
    {
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();

        var building = new Building { Name = "Test Building" };
        context.Buildings.Add(building);
        await context.SaveChangesAsync();

        var room = new Room { Name = "Room 101", Code = "R101", Capacity = 10, BuildingId = building.Id };
        context.Rooms.Add(room);
        await context.SaveChangesAsync();

        var start = DateTimeOffset.UtcNow.AddHours(1);
        var end = start.AddHours(2);
        context.Reservations.Add(new Reservation
        {
            RoomId = room.Id,
            StartTimeUtc = start,
            EndTimeUtc = end,
            Purpose = "First",
            Status = ReservationStatus.Approved,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        context.Reservations.Add(new Reservation
        {
            RoomId = room.Id,
            StartTimeUtc = start.AddMinutes(30),
            EndTimeUtc = end.AddMinutes(30),
            Purpose = "Overlap",
            Status = ReservationStatus.Approved,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new AppDbContext(options);
    }
}
