using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using eduHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace eduHub.IntegrationTests;

public sealed class ApiTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("eduhub_api_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Test factory is not initialized.");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = BuildFactory();
        await EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _postgres.DisposeAsync();
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
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE buildings, rooms, reservations, users, refresh_tokens, revoked_tokens RESTART IDENTITY CASCADE;");
    }

    private WebApplicationFactory<Program> BuildFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var settings = new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                        ["Jwt:Key"] = "test-key-test-key-test-key-test-key-1234",
                        ["Jwt:Issuer"] = "eduHub",
                        ["Jwt:Audience"] = "eduHub",
                        ["Jwt:AccessTokenMinutes"] = "15",
                        ["Jwt:RefreshTokenDays"] = "7",
                        ["Startup:AutoMigrate"] = "true",
                        ["Seed:Enabled"] = "false",
                        ["Seed:Admin:Enabled"] = "false",
                        ["Seed:SampleData:Enabled"] = "false"
                    };

                    config.AddInMemoryCollection(settings);
                });
            });
    }

    private async Task EnsureDatabaseAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE buildings, rooms, reservations, users, refresh_tokens, revoked_tokens RESTART IDENTITY CASCADE;");
    }
}
