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
using eduHub.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace eduHub.IntegrationTests;

public class ServiceIntegrationTests : IAsyncLifetime
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
    public async Task RefreshTokenReuse_RevokesAllTokensForUser()
    {
        var (context, tenant) = await CreateDbContextAsync();
        await using (context)
        {
            var org = await EnsureOrganizationAsync(context);
            var jwtOptions = BuildJwtOptions();
            var userService = new UserService(context, jwtOptions);

            var password = NewPassword();
            var registered = await userService.RegisterAsync(new UserRegisterDto
            {
                UserName = $"user-{Guid.NewGuid():N}",
                Email = $"user-{Guid.NewGuid():N}@example.com",
                Password = password
            });

            await AddMembershipAsync(context, tenant, org.Id, registered.Id, OrganizationMemberRole.User);

            var auth = await userService.LoginAsync(new UserLoginDto
            {
                UserNameOrEmail = registered.UserName,
                Password = password
            }, org.Id);

            Assert.NotNull(auth);

            var refreshed = await userService.RefreshAsync(new RefreshRequestDto
            {
                RefreshToken = auth!.RefreshToken
            }, org.Id);

            Assert.NotNull(refreshed);

            var reused = await userService.RefreshAsync(new RefreshRequestDto
            {
                RefreshToken = auth.RefreshToken
            }, org.Id);

            Assert.Null(reused);

            var tokens = await context.RefreshTokens
                .Where(rt => rt.UserId == registered.Id)
                .ToListAsync();

            Assert.NotEmpty(tokens);
            Assert.All(tokens, token => Assert.NotNull(token.RevokedAtUtc));
        }
    }

    [Fact]
    public async Task LogoutRevokesRefreshTokensForUser()
    {
        var (context, tenant) = await CreateDbContextAsync();
        await using (context)
        {
            var org = await EnsureOrganizationAsync(context);
            var jwtOptions = BuildJwtOptions();
            var userService = new UserService(context, jwtOptions);

            var password = NewPassword();
            var registered = await userService.RegisterAsync(new UserRegisterDto
            {
                UserName = $"user-{Guid.NewGuid():N}",
                Email = $"user-{Guid.NewGuid():N}@example.com",
                Password = password
            });

            await AddMembershipAsync(context, tenant, org.Id, registered.Id, OrganizationMemberRole.User);

            var auth = await userService.LoginAsync(new UserLoginDto
            {
                UserNameOrEmail = registered.UserName,
                Password = password
            }, org.Id);

            Assert.NotNull(auth);

            await userService.RevokeRefreshTokensAsync(registered.Id);

            var tokens = await context.RefreshTokens
                .Where(rt => rt.UserId == registered.Id)
                .ToListAsync();

            Assert.NotEmpty(tokens);
            Assert.All(tokens, token => Assert.NotNull(token.RevokedAtUtc));
        }
    }

    [Fact]
    public async Task CreateReservation_MissingRoom_Throws()
    {
        var (context, tenant) = await CreateDbContextAsync();
        await using (context)
        {
            var org = await EnsureOrganizationAsync(context);
            tenant.SetTenant(org.Id);
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
    }

    [Fact]
    public async Task CreateReservation_DeletedRoom_Throws()
    {
        var (context, tenant) = await CreateDbContextAsync();
        await using (context)
        {
            var org = await EnsureOrganizationAsync(context);
            tenant.SetTenant(org.Id);
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
    }

    [Fact]
    public async Task GetAvailableRooms_IgnoresRejectedReservations()
    {
        var (context, tenant) = await CreateDbContextAsync();
        await using (context)
        {
            var org = await EnsureOrganizationAsync(context);
            tenant.SetTenant(org.Id);
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
    }

    [Fact]
    public async Task BuildingCursor_ReturnsNextPage()
    {
        var (context, tenant) = await CreateDbContextAsync();
        await using (context)
        {
            var org = await EnsureOrganizationAsync(context);
            tenant.SetTenant(org.Id);
            var service = new BuildingService(context, tenant);

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
    }

    [Fact]
    public async Task QueryFilters_Block_CrossTenant_Reads()
    {
        var (context, tenant) = await CreateDbContextAsync();
        await using (context)
        {
            var orgA = await EnsureOrganizationAsync(context);
            var orgB = await EnsureOrganizationAsync(context);

            tenant.SetTenant(orgA.Id);
            context.Buildings.Add(new Building { Name = "Org-A-Building" });
            await context.SaveChangesAsync();

            tenant.SetTenant(orgB.Id);
            context.Buildings.Add(new Building { Name = "Org-B-Building" });
            await context.SaveChangesAsync();

            tenant.SetTenant(orgA.Id);
            var buildings = await context.Buildings.AsNoTracking().ToListAsync();

            Assert.Single(buildings);
            Assert.Contains(buildings, b => b.Name == "Org-A-Building");
        }
    }

    private async Task<(AppDbContext Context, CurrentTenant Tenant)> CreateDbContextAsync()
    {
        var tenant = new CurrentTenant();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        var context = new AppDbContext(options, tenant);
        await context.Database.MigrateAsync();
        await ResetDatabaseAsync(context);
        return (context, tenant);
    }

    private static async Task ResetDatabaseAsync(AppDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE audit_logs, organization_invites, organization_members, organizations, buildings, rooms, reservations, users, refresh_tokens, revoked_tokens RESTART IDENTITY CASCADE;");
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

    private static async Task<Organization> EnsureOrganizationAsync(AppDbContext context)
    {
        var slug = $"org-{Guid.NewGuid():N}";
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            Slug = slug,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org;
    }

    private static async Task AddMembershipAsync(
        AppDbContext context,
        CurrentTenant tenant,
        Guid organizationId,
        int userId,
        OrganizationMemberRole role)
    {
        tenant.SetTenant(organizationId);
        try
        {
            context.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organizationId,
                UserId = userId,
                Role = role,
                Status = OrganizationMemberStatus.Active,
                JoinedAtUtc = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }
        finally
        {
            tenant.Clear();
        }
    }
}
