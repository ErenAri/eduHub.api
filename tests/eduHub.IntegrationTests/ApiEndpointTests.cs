using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using eduHub.Application.DTOs.Buildings;
using eduHub.Application.DTOs.Organizations;
using eduHub.Application.DTOs.Reservations;
using eduHub.Application.DTOs.Rooms;
using eduHub.Application.DTOs.Tenants;
using eduHub.Application.DTOs.Users;
using eduHub.Application.Interfaces.Tenants;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace eduHub.IntegrationTests;

[Collection("Api")]
public class ApiEndpointTests
{
    private static int _ipCounter = 10;
    private const string DefaultTenantSlug = "alpha";
    private readonly ApiTestFixture _fixture;

    public ApiEndpointTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Health_Live_ReturnsOk()
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.CreateClient(forwardedFor: NextClientIp());

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_Ready_RequiresAuth()
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.CreateClient(forwardedFor: NextClientIp());

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TenantResolve_ReturnsTenant_ForKnownEmail()
    {
        await _fixture.ResetDatabaseAsync();

        var email = $"resolve-{Guid.NewGuid():N}@example.com";
        var org = await EnsureOrganizationAsync("resolve");
        await CreateUserAsync(UserRole.User, OrganizationMemberRole.User, org.Id, NewPassword(), email);

        using var client = _fixture.CreateClient(forwardedFor: NextClientIp());
        var resolveResponse = await client.PostAsJsonAsync(
            "/api/tenant/resolve",
            new TenantResolveRequestDto { Email = email });

        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        var resolve = await resolveResponse.Content.ReadFromJsonAsync<TenantResolveRequestResponseDto>();
        Assert.NotNull(resolve);
        Assert.True(resolve!.VerificationSent);
        Assert.False(string.IsNullOrWhiteSpace(resolve.DebugToken));

        var verifyResponse = await client.PostAsJsonAsync(
            "/api/tenant/resolve/verify",
            new TenantResolveVerifyRequestDto { Token = resolve.DebugToken! });

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verify = await verifyResponse.Content.ReadFromJsonAsync<TenantResolveResponseDto>();
        Assert.NotNull(verify);
        Assert.Contains(verify!.Tenants, tenant => tenant.Id == org.Id);
    }

    [Fact]
    public async Task TenantResolve_Token_IsSingleUse()
    {
        await _fixture.ResetDatabaseAsync();

        var email = $"resolve-{Guid.NewGuid():N}@example.com";
        var org = await EnsureOrganizationAsync("resolve-single");
        await CreateUserAsync(UserRole.User, OrganizationMemberRole.User, org.Id, NewPassword(), email);

        using var client = _fixture.CreateClient(forwardedFor: NextClientIp());
        var resolveResponse = await client.PostAsJsonAsync(
            "/api/tenant/resolve",
            new TenantResolveRequestDto { Email = email });

        var resolve = await resolveResponse.Content.ReadFromJsonAsync<TenantResolveRequestResponseDto>();
        Assert.NotNull(resolve);
        Assert.False(string.IsNullOrWhiteSpace(resolve!.DebugToken));

        await client.PostAsJsonAsync(
            "/api/tenant/resolve/verify",
            new TenantResolveVerifyRequestDto { Token = resolve.DebugToken! });

        var secondVerifyResponse = await client.PostAsJsonAsync(
            "/api/tenant/resolve/verify",
            new TenantResolveVerifyRequestDto { Token = resolve.DebugToken! });

        Assert.Equal(HttpStatusCode.OK, secondVerifyResponse.StatusCode);
        var secondVerify = await secondVerifyResponse.Content.ReadFromJsonAsync<TenantResolveResponseDto>();
        Assert.NotNull(secondVerify);
        Assert.Empty(secondVerify!.Tenants);
    }

    [Fact]
    public async Task TenantResolve_InvalidEmail_ReturnsBadRequest()
    {
        await _fixture.ResetDatabaseAsync();

        using var client = _fixture.CreateClient(forwardedFor: NextClientIp());
        var response = await client.PostAsJsonAsync(
            "/api/tenant/resolve",
            new TenantResolveRequestDto { Email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TenantResolve_UnknownToken_ReturnsEmpty()
    {
        await _fixture.ResetDatabaseAsync();

        using var client = _fixture.CreateClient();
        var verifyResponse = await client.PostAsJsonAsync(
            "/api/tenant/resolve/verify",
            new TenantResolveVerifyRequestDto { Token = $"missing-{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verify = await verifyResponse.Content.ReadFromJsonAsync<TenantResolveResponseDto>();
        Assert.NotNull(verify);
        Assert.Empty(verify!.Tenants);
    }

    [Fact]
    public async Task TenantResolve_ExpiredToken_ReturnsEmpty()
    {
        await _fixture.ResetDatabaseAsync();

        var token = $"expired-{Guid.NewGuid():N}";
        var email = $"expired-{Guid.NewGuid():N}@example.com";
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TenantResolveTokens.Add(new TenantResolveToken
            {
                Id = Guid.NewGuid(),
                Email = email.ToLowerInvariant(),
                TokenHash = HashToken(token),
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            });
            await db.SaveChangesAsync();
        }

        using var client = _fixture.CreateClient();
        var verifyResponse = await client.PostAsJsonAsync(
            "/api/tenant/resolve/verify",
            new TenantResolveVerifyRequestDto { Token = token });

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verify = await verifyResponse.Content.ReadFromJsonAsync<TenantResolveResponseDto>();
        Assert.NotNull(verify);
        Assert.Empty(verify!.Tenants);
    }

    [Fact]
    public async Task Login_ReturnsToken()
    {
        await _fixture.ResetDatabaseAsync();

        var password = NewPassword();
        var org = await EnsureOrganizationAsync(DefaultTenantSlug);
        var user = await CreateUserAsync(UserRole.User, OrganizationMemberRole.User, org.Id, password);
        using var client = _fixture.CreateClient(org.Slug, NextClientIp());

        var auth = await LoginAsync(client, user.UserName, password);

        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
    }

    [Fact]
    public async Task Admin_Can_Create_Building()
    {
        await _fixture.ResetDatabaseAsync();

        var password = NewPassword();
        var org = await EnsureOrganizationAsync(DefaultTenantSlug);
        var admin = await CreateUserAsync(UserRole.User, OrganizationMemberRole.OrgAdmin, org.Id, password);
        using var client = _fixture.CreateClient(org.Slug, NextClientIp());
        var auth = await LoginAsync(client, admin.UserName, password);
        SetBearer(client, auth.AccessToken);

        var created = await CreateBuildingAsync(client, $"Building-{Guid.NewGuid():N}");

        Assert.True(created.Id > 0);
    }

    [Fact]
    public async Task Admin_Can_Create_Room()
    {
        await _fixture.ResetDatabaseAsync();

        var password = NewPassword();
        var org = await EnsureOrganizationAsync(DefaultTenantSlug);
        var admin = await CreateUserAsync(UserRole.User, OrganizationMemberRole.OrgAdmin, org.Id, password);
        using var client = _fixture.CreateClient(org.Slug, NextClientIp());
        var auth = await LoginAsync(client, admin.UserName, password);
        SetBearer(client, auth.AccessToken);

        var building = await CreateBuildingAsync(client, $"Building-{Guid.NewGuid():N}");
        var room = await CreateRoomAsync(client, building.Id);

        Assert.True(room.Id > 0);
        Assert.Equal(building.Id, room.BuildingId);
    }

    [Fact]
    public async Task User_Can_Create_Reservation()
    {
        await _fixture.ResetDatabaseAsync();

        var org = await EnsureOrganizationAsync(DefaultTenantSlug);
        var adminPassword = NewPassword();
        var admin = await CreateUserAsync(UserRole.User, OrganizationMemberRole.OrgAdmin, org.Id, adminPassword);
        var userPassword = NewPassword();
        var user = await CreateUserAsync(UserRole.User, OrganizationMemberRole.User, org.Id, userPassword);

        using var adminClient = _fixture.CreateClient(org.Slug, NextClientIp());
        var adminAuth = await LoginAsync(adminClient, admin.UserName, adminPassword);
        SetBearer(adminClient, adminAuth.AccessToken);

        var building = await CreateBuildingAsync(adminClient, $"Building-{Guid.NewGuid():N}");
        var room = await CreateRoomAsync(adminClient, building.Id);

        using var userClient = _fixture.CreateClient(org.Slug, NextClientIp());
        var userAuth = await LoginAsync(userClient, user.UserName, userPassword);
        SetBearer(userClient, userAuth.AccessToken);

        var start = DateTimeOffset.UtcNow.AddHours(2);
        var end = start.AddHours(1);
        var dto = new ReservationCreateDto
        {
            RoomId = room.Id,
            StartTimeUtc = start,
            EndTimeUtc = end,
            Purpose = "Study session"
        };

        var response = await userClient.PostAsJsonAsync("/api/org/reservations", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var reservation = await response.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(reservation);
        Assert.Equal(room.Id, reservation!.RoomId);
    }

    [Fact]
    public async Task Admin_Can_Approve_Reservation()
    {
        await _fixture.ResetDatabaseAsync();

        var org = await EnsureOrganizationAsync(DefaultTenantSlug);
        var adminPassword = NewPassword();
        var admin = await CreateUserAsync(UserRole.User, OrganizationMemberRole.OrgAdmin, org.Id, adminPassword);
        var userPassword = NewPassword();
        var user = await CreateUserAsync(UserRole.User, OrganizationMemberRole.User, org.Id, userPassword);

        using var adminClient = _fixture.CreateClient(org.Slug, NextClientIp());
        var adminAuth = await LoginAsync(adminClient, admin.UserName, adminPassword);
        SetBearer(adminClient, adminAuth.AccessToken);

        var building = await CreateBuildingAsync(adminClient, $"Building-{Guid.NewGuid():N}");
        var room = await CreateRoomAsync(adminClient, building.Id);

        using var userClient = _fixture.CreateClient(org.Slug, NextClientIp());
        var userAuth = await LoginAsync(userClient, user.UserName, userPassword);
        SetBearer(userClient, userAuth.AccessToken);

        var start = DateTimeOffset.UtcNow.AddHours(2);
        var end = start.AddHours(1);
        var dto = new ReservationCreateDto
        {
            RoomId = room.Id,
            StartTimeUtc = start,
            EndTimeUtc = end,
            Purpose = "Review"
        };

        var createResponse = await userClient.PostAsJsonAsync("/api/org/reservations", dto);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var reservation = await createResponse.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(reservation);

        var approveResponse = await adminClient.PostAsync($"/api/org/reservations/{reservation!.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = await approveResponse.Content.ReadFromJsonAsync<ReservationResponseDto>();

        Assert.NotNull(approved);
        Assert.Equal("Approved", approved!.Status);
    }

    [Fact]
    public async Task Login_RateLimit_Returns_429_On_6th_Request()
    {
        await _fixture.ResetDatabaseAsync();

        var password = NewPassword();
        var org = await EnsureOrganizationAsync(DefaultTenantSlug);
        var user = await CreateUserAsync(UserRole.User, OrganizationMemberRole.User, org.Id, password);
        using var client = _fixture.CreateClient(org.Slug, "10.0.0.42");

        var dto = new UserLoginDto
        {
            UserNameOrEmail = user.UserName,
            Password = password
        };

        for (var i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync("/api/org/auth/login", dto);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var throttled = await client.PostAsJsonAsync("/api/org/auth/login", dto);

        Assert.Equal((HttpStatusCode)429, throttled.StatusCode);
    }

    [Fact]
    public async Task User_Cannot_Access_Other_Tenant_Data()
    {
        await _fixture.ResetDatabaseAsync();

        var orgA = await EnsureOrganizationAsync("alpha");
        var orgB = await EnsureOrganizationAsync("beta");
        var password = NewPassword();
        var user = await CreateUserAsync(UserRole.User, OrganizationMemberRole.OrgAdmin, orgA.Id, password);

        using var clientA = _fixture.CreateClient(orgA.Slug, NextClientIp());
        var auth = await LoginAsync(clientA, user.UserName, password);
        SetBearer(clientA, auth.AccessToken);

        var building = await CreateBuildingAsync(clientA, $"Building-{Guid.NewGuid():N}");

        using var clientB = _fixture.CreateClient(orgB.Slug, NextClientIp());
        SetBearer(clientB, auth.AccessToken);

        var response = await clientB.GetAsync($"/api/org/buildings/{building.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PlatformAdmin_Can_List_Organizations()
    {
        await _fixture.ResetDatabaseAsync();

        var orgA = await EnsureOrganizationAsync("alpha");
        var orgB = await EnsureOrganizationAsync("beta");

        var password = NewPassword();
        var admin = await CreateUserAsync(UserRole.Admin, OrganizationMemberRole.OrgAdmin, orgA.Id, password);

        using var orgClient = _fixture.CreateClient(orgA.Slug, NextClientIp());
        var auth = await LoginAsync(orgClient, admin.UserName, password);

        using var platformClient = _fixture.CreateClient();
        SetBearer(platformClient, auth.AccessToken);

        var response = await platformClient.GetAsync("/api/platform/organizations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var orgs = await response.Content.ReadFromJsonAsync<OrganizationResponseDto[]>();
        Assert.NotNull(orgs);
        Assert.Contains(orgs!, o => o.Id == orgA.Id);
        Assert.Contains(orgs!, o => o.Id == orgB.Id);
    }

    private static string NextClientIp()
    {
        var next = Interlocked.Increment(ref _ipCounter);
        var octet = (next % 200) + 10;
        return $"10.0.0.{octet}";
    }

    private static string NewPassword()
    {
        return $"Pass-{Guid.NewGuid():N}!";
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    private async Task<User> CreateUserAsync(
        UserRole role,
        OrganizationMemberRole orgRole,
        Guid organizationId,
        string password,
        string? emailOverride = null)
    {
        var userName = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}";
        var email = emailOverride ?? $"{userName}@example.com";
        var user = new User
        {
            UserName = userName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            CreatedAtUtc = DateTime.UtcNow
        };

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();      
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var tenantSetter = scope.ServiceProvider.GetRequiredService<ICurrentTenantSetter>();
        tenantSetter.SetTenant(organizationId);
        try
        {
            db.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organizationId,
                UserId = user.Id,
                Role = orgRole,
                Status = OrganizationMemberStatus.Active,
                JoinedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        finally
        {
            tenantSetter.Clear();
        }

        return user;
    }

    private async Task<Organization> EnsureOrganizationAsync(string slug)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Organizations.FirstOrDefaultAsync(o => o.Slug == slug);
        if (existing != null)
            return existing;

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = $"{slug}-org",
            Slug = slug,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return org;
    }

    private static async Task<AuthResponseDto> LoginAsync(HttpClient client, string userNameOrEmail, string password)
    {
        var response = await client.PostAsJsonAsync("/api/org/auth/login", new UserLoginDto
        {
            UserNameOrEmail = userNameOrEmail,
            Password = password
        });

        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        return auth!;
    }

    private static void SetBearer(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static async Task<BuildingResponseDto> CreateBuildingAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/org/buildings", new BuildingCreateDto
        {
            Name = name
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<BuildingResponseDto>();
        Assert.NotNull(created);
        return created!;
    }

    private static async Task<RoomResponseDto> CreateRoomAsync(HttpClient client, int buildingId)
    {
        var response = await client.PostAsJsonAsync("/api/org/rooms", new RoomCreateDto
        {
            Code = $"R-{Guid.NewGuid():N}",
            Name = $"Room-{Guid.NewGuid():N}",
            Capacity = 20,
            BuildingId = buildingId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RoomResponseDto>();
        Assert.NotNull(created);
        return created!;
    }
}
