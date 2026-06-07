using AssetMap.Entities.Enums;

namespace AssetMap.Entities;

public class WatchedWallet
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public BlockchainNetwork Network { get; set; }
    public string Address { get; set; } = null!;
    public DateTime? LastSyncedAt { get; set; }
    public SyncStatus SyncStatus { get; set; }

    public Account Account { get; set; } = null!;
}
