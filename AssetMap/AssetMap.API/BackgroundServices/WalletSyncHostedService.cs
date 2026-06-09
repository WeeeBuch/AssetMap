using AssetMap.Core.Services;

namespace AssetMap.API.BackgroundServices;

/// <summary>
/// Při startu a každé 2 hodiny synchronizuje pending krypto peněženky.
/// </summary>
public class WalletSyncHostedService(
    IServiceProvider               services,
    ILogger<WalletSyncHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Krátká pauza při startu — necháme SnapshotService projít první
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        await RunCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunCycleAsync(stoppingToken);
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var sync = scope.ServiceProvider.GetRequiredService<IWalletSyncService>();
            await sync.SyncPendingWalletsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "WalletSync cycle failed");
        }
    }
}
