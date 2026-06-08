using AssetMap.Database;
using AssetMap.Entities;
using Microsoft.EntityFrameworkCore;

namespace AssetMap.Core.Services;

public class PortfolioService(AppDbContext db) : IPortfolioService
{
    public async Task TakeSnapshotAsync(Guid accountId, CancellationToken ct = default)
    {
        // Spočítej aktuální USD hodnotu z Holdings × nejnovější PriceSnapshot
        var holdings = await db.Holdings
            .Where(h => h.AccountId == accountId)
            .Include(h => h.Asset)
            .ToListAsync(ct);

        if (holdings.Count == 0) return;

        var account = await db.Accounts.FindAsync([accountId], ct);
        if (account is null) return;

        double totalUsd = 0;
        foreach (var h in holdings)
        {
            var price = await db.PriceSnapshots
                .Where(p => p.AssetId == h.AssetId)
                .OrderByDescending(p => p.Timestamp)
                .FirstOrDefaultAsync(ct);

            double priceUsd = price is not null ? (double)price.Price : 1.0;
            totalUsd += (double)h.Quantity * priceUsd;
        }

        db.PortfolioSnapshots.Add(new PortfolioSnapshot
        {
            Id         = Guid.NewGuid(),
            UserId     = account.UserId,
            AccountId  = accountId,
            TotalValue = (decimal)totalUsd,
            Timestamp  = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<double[]> GetHistoryUsdAsync(
        Guid accountId, int days = 365, CancellationToken ct = default)
    {
        DateTime since = DateTime.UtcNow.Date.AddDays(-days + 1);

        var snapshots = await db.PortfolioSnapshots
            .Where(p => p.AccountId == accountId && p.Timestamp >= since)
            .OrderBy(p => p.Timestamp)
            .ToListAsync(ct);

        if (snapshots.Count == 0)
            return [];

        // Forward-fill: 1 hodnota per den
        var result  = new double[days];
        double last = (double)snapshots[0].TotalValue;
        int    si   = 0;

        for (int i = 0; i < days; i++)
        {
            DateTime day = since.AddDays(i);

            // Posuň na nejnovější snapshot v tento den
            while (si < snapshots.Count &&
                   snapshots[si].Timestamp.Date <= day)
            {
                last = (double)snapshots[si].TotalValue;
                si++;
            }

            result[i] = last;
        }

        return result;
    }

    public async Task TakeAllSnapshotsAsync(Guid userId, CancellationToken ct = default)
    {
        var accountIds = await db.Accounts
            .Where(a => a.UserId == userId && !a.IsArchived)
            .Select(a => a.Id)
            .ToListAsync(ct);

        foreach (var id in accountIds)
            await TakeSnapshotAsync(id, ct);
    }
}
