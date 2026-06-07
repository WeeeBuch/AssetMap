namespace AssetMap.Entities;

public class PriceSnapshot
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public decimal Price { get; set; }
    public DateTime Timestamp { get; set; }

    public Asset Asset { get; set; } = null!;
}
