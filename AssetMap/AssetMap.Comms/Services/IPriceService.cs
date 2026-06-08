using AssetMap.Entities.Enums;

namespace AssetMap.Comms.Services;

public record PriceResult(
    string  Symbol,
    string  Name,
    double  PriceUsd,
    bool    Found
);

/// <summary>
/// Jednotný price lookup: fiat → FxRates, crypto → CoinGecko, stock/ETF → Yahoo Finance.
/// </summary>
public interface IPriceService
{
    Task<PriceResult> GetPriceAsync(string symbol, AssetType assetType, CancellationToken ct = default);
}
