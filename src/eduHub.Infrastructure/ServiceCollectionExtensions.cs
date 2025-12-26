using eduHub.Application.Interfaces.Buildings;
using eduHub.Application.Interfaces.Organizations;
using eduHub.Application.Interfaces.Reservations;
using eduHub.Application.Interfaces.Rooms;
using eduHub.Application.Interfaces.Tenants;
using eduHub.Application.Interfaces.Users;
using eduHub.Infrastructure.Persistence;
using eduHub.Infrastructure.Services;
using eduHub.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace eduHub.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<CurrentTenant>();
        services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenant>());
        services.AddScoped<ICurrentTenantSetter>(sp => sp.GetRequiredService<CurrentTenant>());

        services.AddScoped<IBuildingService, BuildingService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IOrganizationInviteService, OrganizationInviteService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IUserService, UserService>();

        return services;
    }
}
