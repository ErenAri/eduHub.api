using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Net.Http;
using System.Threading.Tasks;
using eduHub.api;
using eduHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace eduHub.IntegrationTests;

public sealed class ApiTestFixture : IAsyncLifetime
{
    private SqliteConnection? _connection;
    private WebApplicationFactory<Program>? _factory;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Test factory is not initialized.");

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _factory = BuildFactory();
        await EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
    }

    public HttpClient CreateClient(string? forwardedFor = null)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        if (!string.IsNullOrWhiteSpace(forwardedFor))
            client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);

        return client;
    }

    public async Task ResetDatabaseAsync()
    {
        // For SQLite in-memory, we can't easily TRUNCATE without dropping the schema if we are not careful.
        // But simply deleting all rows is often enough.
        // OR we can just re-create the schema.
        // However, EnsureDatabaseAsync calls Database.EnsureCreatedAsync which might be faster.

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Clear all tables
        db.Reservations.RemoveRange(db.Reservations);
        db.Rooms.RemoveRange(db.Rooms);
        db.Buildings.RemoveRange(db.Buildings);
        db.RevokedTokens.RemoveRange(db.RevokedTokens);
        db.RefreshTokens.RemoveRange(db.RefreshTokens);
        db.Users.RemoveRange(db.Users);

        await db.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> BuildFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var jwtKey = NewJwtKey();
                    var settings = new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = "DataSource=:memory:",
                        ["Jwt:Key"] = jwtKey,
                        ["Jwt:Issuer"] = "eduHub",
                        ["Jwt:Audience"] = "eduHub",
                        ["Jwt:AccessTokenMinutes"] = "15",
                        ["Jwt:RefreshTokenDays"] = "7",
                        ["Startup:AutoMigrate"] = "false", // We handle this manually
                        ["Seed:Enabled"] = "false",
                        ["Seed:Admin:Enabled"] = "false",
                        ["Seed:SampleData:Enabled"] = "false"
                    };

                    config.AddInMemoryCollection(settings);
                });

                builder.ConfigureTestServices(services =>
                {
                    // Remove the existing DbContext registration (which uses Npgsql)
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));

                    // Add the DbContext using SQLite
                    services.AddDbContext<AppDbContext>(options =>
                    {
                        options.UseSqlite(_connection!);
                    });
                });
            });
    }

    private async Task EnsureDatabaseAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private static string NewJwtKey()
    {
        return $"{Guid.NewGuid():N}{Guid.NewGuid():N}";
    }
}
