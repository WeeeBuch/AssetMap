using AssetMap.Comms.Services;
using AssetMap.Core.Services;
using AssetMap.Database;
using AssetMap.Entities;
using Microsoft.EntityFrameworkCore;

namespace AssetMap.API.BackgroundServices;

/// <summary>
/// Každou hodinu:
///   1. Obnoví kurzy měn (FxRates + uloží PriceSnapshot do DB)
///   2. Uloží PortfolioSnapshot pro všechny účty
/// </summary>
public class SnapshotHostedService(
    IServiceProvider   services,
    ILogger<SnapshotHostedService> logger)
    : BackgroundService
{
    // Zachováno pro zpětnou kompatibilitu — nový kód čte UserId přímo z DB
    private static readonly Guid _fallbackUserId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Fiat symboly, jejichž PriceSnapshot udržujeme aktuální
    private static readonly string[] KnownFiats =
        ["CZK", "EUR", "GBP", "CHF", "JPY", "PLN", "USD"];

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
            var            db         = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 1. Načti čerstvé kurzy do paměti
            await fx.RefreshAsync(ct);

            // 2. Uložit čerstvé kurzy jako PriceSnapshot pro každé fiat aktivum v DB
            //    → TakeAllSnapshotsAsync pak použije tyto aktuální ceny, ne stará data
            await PersistFiatPriceSnapshotsAsync(db, fx, ct);

            // 3. Ulož PortfolioSnapshot pro všechny uživatele
            var userIds = await db.Users.Select(u => u.Id).ToListAsync(ct);
            foreach (var uid in userIds)
                await portfolio.TakeAllSnapshotsAsync(uid, ct);

            logger.LogInformation("Snapshot cycle completed at {Time}", DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Snapshot cycle failed");
        }
    }

    /// <summary>
    /// Pro každý fiat symbol, který existuje v DB jako Asset, přidá PriceSnapshot
    /// s aktuální cenou z FxRateService. Zajišťuje, že portfoliové snapshoty
    /// budou vypočteny správným kurzem i při prvním spuštění.
    /// </summary>
    private static async Task PersistFiatPriceSnapshotsAsync(
        AppDbContext db, IFxRateService fx, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        foreach (string code in KnownFiats)
        {
            var asset = await db.Assets
                .FirstOrDefaultAsync(a => a.Symbol == code, ct);

            if (asset is null) continue;   // aktivum ještě neexistuje — skip

            double fiatToUsd = fx.FiatToUsd(code);
            if (fiatToUsd <= 0) continue;

            db.PriceSnapshots.Add(new PriceSnapshot
            {
                Id        = Guid.NewGuid(),
                AssetId   = asset.Id,
                Price     = (decimal)fiatToUsd,
                Timestamp = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
