namespace AssetMap.Entities;

public class Holding
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid AssetId { get; set; }
    public decimal Quantity { get; set; }
    public decimal CostBasis { get; set; }

    public Account Account { get; set; } = null!;
    public Asset Asset { get; set; } = null!;
}
