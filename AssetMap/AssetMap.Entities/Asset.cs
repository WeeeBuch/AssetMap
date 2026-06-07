using AssetMap.Entities.Enums;

namespace AssetMap.Entities;

public class Asset
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = null!;
    public string Name { get; set; } = null!;
    public AssetType AssetType { get; set; }

    public ICollection<Holding> Holdings { get; set; } = [];
    public ICollection<Transaction> Transactions { get; set; } = [];
    public ICollection<PriceSnapshot> PriceSnapshots { get; set; } = [];
    public ICollection<AssetPriceFeed> PriceFeeds { get; set; } = [];
}
