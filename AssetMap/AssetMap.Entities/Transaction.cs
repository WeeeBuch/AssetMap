using AssetMap.Entities.Enums;

namespace AssetMap.Entities;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public Guid AssetId { get; set; }
    public decimal Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
    public Guid? CounterAssetId { get; set; }
    public decimal? CounterQuantity { get; set; }
    public decimal? Fee { get; set; }
    public Guid? FeeAssetId { get; set; }
    public Guid? RelatedAssetId { get; set; }
    public Guid? FromAccountId { get; set; }
    public Guid? ToAccountId { get; set; }
    public Guid? ImportBatchId { get; set; }
    public string? Note { get; set; }
    public string? Category { get; set; }

    public Account Account { get; set; } = null!;
    public Asset Asset { get; set; } = null!;
    public Asset? CounterAsset { get; set; }
    public Asset? FeeAsset { get; set; }
    public Asset? RelatedAsset { get; set; }
    public Account? FromAccount { get; set; }
    public Account? ToAccount { get; set; }
    public ImportBatch? ImportBatch { get; set; }
}
