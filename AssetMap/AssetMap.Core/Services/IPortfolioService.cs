namespace AssetMap.Core.Services;

public interface IPortfolioService
{
    /// <summary>
    /// Uloží snapshot aktuální USD hodnoty účtu.
    /// Voláno: každou hodinu background service + po každé transakci.
    /// </summary>
    Task TakeSnapshotAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Vrátí pole USD hodnot délky <paramref name="days"/>, nejstarší první.
    /// Forward-filluje chybějící dny.
    /// </summary>
    Task<double[]> GetHistoryUsdAsync(Guid accountId, int days = 365, CancellationToken ct = default);

    /// <summary>Spustí snapshoty pro všechny účty daného uživatele.</summary>
    Task TakeAllSnapshotsAsync(Guid userId, CancellationToken ct = default);
}
