using eduHub.api.Options;
using eduHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace eduHub.api.HostedServices;

public sealed class TokenCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TokenCleanupService> _logger;
    private readonly TokenCleanupOptions _options;

    public TokenCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<TokenCleanupService> logger,
        IOptions<TokenCleanupOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Token cleanup is disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(_options.IntervalMinutes);
        if (interval < TimeSpan.FromMinutes(5))
            interval = TimeSpan.FromMinutes(5);

        await RunCleanupSafeAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCleanupSafeAsync(stoppingToken);
        }
    }

    private async Task RunCleanupSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunCleanupAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token cleanup failed.");
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        var refreshDeleted = await db.RefreshTokens
            .Where(rt => rt.ExpiresAtUtc <= now || rt.RevokedAtUtc != null)
            .ExecuteDeleteAsync(cancellationToken);

        var revokedDeleted = await db.RevokedTokens
            .Where(rt => rt.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);

        if (refreshDeleted > 0 || revokedDeleted > 0)
        {
            _logger.LogInformation(
                "Token cleanup removed {RefreshCount} refresh tokens and {RevokedCount} revoked tokens.",
                refreshDeleted,
                revokedDeleted);
        }
    }
}
