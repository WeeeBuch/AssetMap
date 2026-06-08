using AssetMap.Entities.Enums;

namespace AssetMap.Entities;

public class Account
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = null!;
    public AccountType AccountType { get; set; }
    public string? Institution { get; set; }
    public string? IconColorHex { get; set; }
    public string  BaseCurrency { get; set; } = "USD";
    public bool IsArchived { get; set; }

    public User User { get; set; } = null!;
    public ICollection<Holding> Holdings { get; set; } = [];
    public ICollection<WatchedWallet> WatchedWallets { get; set; } = [];
    public ICollection<Transaction> Transactions { get; set; } = [];
    public ICollection<ImportBatch> ImportBatches { get; set; } = [];
}
