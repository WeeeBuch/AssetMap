using AssetMap.Entities.Enums;

namespace AssetMap.Entities;

public class ImportBatch
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public DateTime ImportedAt { get; set; }
    public string FileName { get; set; } = null!;
    public int RowCount { get; set; }
    public int SuccessCount { get; set; }
    public ImportBatchStatus Status { get; set; }
    public string? Notes { get; set; }

    public User User { get; set; } = null!;
    public Account Account { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = [];
}
