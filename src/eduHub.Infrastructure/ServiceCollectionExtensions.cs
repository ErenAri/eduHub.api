using Azure.Core;
using Azure.Identity;
using eduHub.Application.Interfaces.Buildings;
using eduHub.Application.Interfaces.Reservations;
using eduHub.Application.Interfaces.Rooms;
using eduHub.Application.Interfaces.Users;
using eduHub.Infrastructure.Persistence;
using eduHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace eduHub.Infrastructure;

public static class ServiceCollectionExtensions
{
    private static readonly TokenRequestContext PgTokenScope =
        new(new[] { "https://ossrdbms-aad.database.windows.net/.default" });

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

        var useManagedIdentity = configuration.GetValue("Database:UseManagedIdentity", false);

        services.AddSingleton<NpgsqlDataSource>(_ =>
        {
            if (!useManagedIdentity)
                return new NpgsqlDataSourceBuilder(connectionString).Build();

            var builder = new NpgsqlDataSourceBuilder(connectionString);
            var credential = new DefaultAzureCredential();

            builder.UsePasswordProvider(
                (host, port, database, username) =>
                    credential.GetToken(PgTokenScope).Token,
                async (host, port, database, username, cancellationToken) =>
                    (await credential.GetTokenAsync(PgTokenScope, cancellationToken)).Token
            );

            return builder.Build();
        });

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
            options.UseNpgsql(dataSource);
        });

        services.AddScoped<IBuildingService, BuildingService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IUserService, UserService>();

        return services;
    }
}
