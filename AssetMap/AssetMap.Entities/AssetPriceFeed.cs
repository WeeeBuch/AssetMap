using AssetMap.Entities.Enums;

namespace AssetMap.Entities;

public class AssetPriceFeed
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public PriceFeedSource Source { get; set; }
    public string? ExternalSymbol { get; set; }
    public int FetchIntervalMinutes { get; set; } = 60;
    public DateTime? LastFetchedAt { get; set; }
    public bool IsEnabled { get; set; } = true;

    public Asset Asset { get; set; } = null!;
}
