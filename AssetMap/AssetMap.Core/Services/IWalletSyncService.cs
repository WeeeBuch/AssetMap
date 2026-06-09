namespace AssetMap.Core.Services;

public interface IWalletSyncService
{
    /// <summary>Synchronizuje všechny peněženky s SyncStatus.Pending.</summary>
    Task SyncPendingWalletsAsync(CancellationToken ct = default);

    /// <summary>Synchronizuje konkrétní peněženku (po vytvoření účtu).</summary>
    Task SyncWalletAsync(Guid walletId, CancellationToken ct = default);
}
