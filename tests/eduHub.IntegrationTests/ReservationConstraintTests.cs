using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using eduHub.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace eduHub.IntegrationTests;

public class ReservationConstraintTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("eduhub_integration")
        .WithUsername($"user_{Guid.NewGuid():N}")
        .WithPassword($"pass_{Guid.NewGuid():N}")
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
        var (context, tenant) = CreateDbContext();
        await using (context)
        {
            await context.Database.MigrateAsync();
            await ResetDatabaseAsync(context);

            var org = await EnsureOrganizationAsync(context);
            tenant.SetTenant(org.Id);

            var building = new Building { Name = "Test Building" };
            context.Buildings.Add(building);
            await context.SaveChangesAsync();

            var room = new Room
            {
                Name = "Room 101",
                Code = "R101",
                Capacity = 10,
                BuildingId = building.Id
            };
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
    }

    private (AppDbContext Context, CurrentTenant Tenant) CreateDbContext()
    {
        var tenant = new CurrentTenant();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return (new AppDbContext(options, tenant), tenant);
    }

    private static async Task ResetDatabaseAsync(AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE audit_logs, organization_invites, organization_members, organizations, buildings, rooms, reservations, users, refresh_tokens, revoked_tokens RESTART IDENTITY CASCADE;");
    }

    private static async Task<Organization> EnsureOrganizationAsync(AppDbContext context)
    {
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = $"org-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org;
    }
}
