using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using eduHub.Application.DTOs.Buildings;
using eduHub.Application.DTOs.Reservations;
using eduHub.Application.DTOs.Rooms;
using eduHub.Application.DTOs.Users;
using eduHub.Domain.Entities;
using eduHub.Domain.Enums;
using eduHub.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace eduHub.IntegrationTests;

[Collection("Api")]
public class ApiEndpointTests
{
    private static int _ipCounter = 10;
    private readonly ApiTestFixture _fixture;

    public ApiEndpointTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Health_Live_ReturnsOk()
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.CreateClient(NextClientIp());

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_Ready_RequiresAuth()
    {
        await _fixture.ResetDatabaseAsync();
        using var client = _fixture.CreateClient(NextClientIp());

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsToken()
    {
        await _fixture.ResetDatabaseAsync();

        var password = NewPassword();
        var user = await CreateUserAsync(UserRole.User, password);
        using var client = _fixture.CreateClient(NextClientIp());

        var auth = await LoginAsync(client, user.UserName, password);

        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
    }

    [Fact]
    public async Task Admin_Can_Create_Building()
    {
        await _fixture.ResetDatabaseAsync();

        var password = NewPassword();
        var admin = await CreateUserAsync(UserRole.Admin, password);
        using var client = _fixture.CreateClient(NextClientIp());
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
        var admin = await CreateUserAsync(UserRole.Admin, password);
        using var client = _fixture.CreateClient(NextClientIp());
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

        var adminPassword = NewPassword();
        var admin = await CreateUserAsync(UserRole.Admin, adminPassword);
        var userPassword = NewPassword();
        var user = await CreateUserAsync(UserRole.User, userPassword);

        using var adminClient = _fixture.CreateClient(NextClientIp());
        var adminAuth = await LoginAsync(adminClient, admin.UserName, adminPassword);
        SetBearer(adminClient, adminAuth.AccessToken);

        var building = await CreateBuildingAsync(adminClient, $"Building-{Guid.NewGuid():N}");
        var room = await CreateRoomAsync(adminClient, building.Id);

        using var userClient = _fixture.CreateClient(NextClientIp());
        var userAuth = await LoginAsync(userClient, user.UserName, userPassword);
        SetBearer(userClient, userAuth.AccessToken);

        var start = DateTimeOffset.UtcNow.AddHours(1);
        var end = start.AddHours(1);
        var dto = new ReservationCreateDto
        {
            RoomId = room.Id,
            StartTimeUtc = start,
            EndTimeUtc = end,
            Purpose = "Study session"
        };

        var response = await userClient.PostAsJsonAsync("/api/reservations", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var reservation = await response.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(reservation);
        Assert.Equal(room.Id, reservation!.RoomId);
    }

    [Fact]
    public async Task Admin_Can_Approve_Reservation()
    {
        await _fixture.ResetDatabaseAsync();

        var adminPassword = NewPassword();
        var admin = await CreateUserAsync(UserRole.Admin, adminPassword);
        var userPassword = NewPassword();
        var user = await CreateUserAsync(UserRole.User, userPassword);

        using var adminClient = _fixture.CreateClient(NextClientIp());
        var adminAuth = await LoginAsync(adminClient, admin.UserName, adminPassword);
        SetBearer(adminClient, adminAuth.AccessToken);

        var building = await CreateBuildingAsync(adminClient, $"Building-{Guid.NewGuid():N}");
        var room = await CreateRoomAsync(adminClient, building.Id);

        using var userClient = _fixture.CreateClient(NextClientIp());
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

        var createResponse = await userClient.PostAsJsonAsync("/api/reservations", dto);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var reservation = await createResponse.Content.ReadFromJsonAsync<ReservationResponseDto>();
        Assert.NotNull(reservation);

        var approveResponse = await adminClient.PostAsync($"/api/reservations/{reservation!.Id}/approve", null);
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
        var user = await CreateUserAsync(UserRole.User, password);
        using var client = _fixture.CreateClient("10.0.0.42");

        var dto = new UserLoginDto
        {
            UserNameOrEmail = user.UserName,
            Password = password
        };

        for (var i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", dto);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var throttled = await client.PostAsJsonAsync("/api/auth/login", dto);

        Assert.Equal((HttpStatusCode)429, throttled.StatusCode);
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

    private async Task<User> CreateUserAsync(UserRole role, string password)
    {
        var userName = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}";
        var email = $"{userName}@example.com";
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
        return user;
    }

    private static async Task<AuthResponseDto> LoginAsync(HttpClient client, string userNameOrEmail, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new UserLoginDto
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
        var response = await client.PostAsJsonAsync("/api/buildings", new BuildingCreateDto
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
        var response = await client.PostAsJsonAsync("/api/rooms", new RoomCreateDto
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
