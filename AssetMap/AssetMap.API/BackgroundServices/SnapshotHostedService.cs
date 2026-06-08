using AssetMap.Comms.Services;
using AssetMap.Core.Services;

namespace AssetMap.API.BackgroundServices;

/// <summary>
/// Každou hodinu:
///   1. Obnoví kurzy měn (FxRates)
///   2. Uloží PortfolioSnapshot pro všechny účty
/// </summary>
public class SnapshotHostedService(
    IServiceProvider   services,
    ILogger<SnapshotHostedService> logger)
    : BackgroundService
{
    private static readonly Guid DefaultUserId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Spusť okamžitě při startu
        await RunCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunCycleAsync(stoppingToken);
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        try
        {
            await using var scope     = services.CreateAsyncScope();
            var            fx         = scope.ServiceProvider.GetRequiredService<IFxRateService>();
            var            portfolio  = scope.ServiceProvider.GetRequiredService<IPortfolioService>();

            await fx.RefreshAsync(ct);
            await portfolio.TakeAllSnapshotsAsync(DefaultUserId, ct);

            logger.LogInformation("Snapshot cycle completed at {Time}", DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Snapshot cycle failed");
        }
    }
}
