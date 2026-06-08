using System.Text.Json;
using AssetMap.Entities.Enums;

namespace AssetMap.Comms.Services;

/// <summary>
/// Routuje price lookup podle typu aktiva:
///   Fiat   → FxRateService
///   Crypto → CoinGecko
///   Stock  → Yahoo Finance
/// </summary>
public class PriceService(IFxRateService fx, HttpClient cryptoHttp, HttpClient stockHttp)
    : IPriceService
{
    // ── Fiat names ────────────────────────────────────────────────
    private static readonly Dictionary<string, string> _fiatNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["CZK"] = "Česká koruna", ["KČ"] = "Česká koruna",
            ["EUR"] = "Euro",         ["USD"] = "Americký dolar",
            ["GBP"] = "Britská libra",["CHF"] = "Švýcarský frank",
            ["JPY"] = "Japonský jen", ["PLN"] = "Polský zlotý",
        };

    public async Task<PriceResult> GetPriceAsync(
        string symbol, AssetType assetType, CancellationToken ct = default)
    {
        string upper = symbol.Trim().ToUpperInvariant();

        return assetType switch
        {
            AssetType.Fiat    => LookupFiat(upper),
            AssetType.Crypto  => await SearchCoinGeckoAsync(upper, ct),
            AssetType.Stock   => await SearchYahooAsync(upper, ct),
            _                 => LookupFiat(upper), // fallback
        };
    }

    // ── Fiat ──────────────────────────────────────────────────────
    private PriceResult LookupFiat(string upper)
    {
        // "KČ" → "CZK"
        string code = upper == "KČ" ? "CZK" : upper;
        if (!_fiatNames.TryGetValue(upper, out string? name))
            return new PriceResult(upper, "", 0, false);
        return new PriceResult(code, name, fx.FiatToUsd(code), true);
    }

    // ── CoinGecko ─────────────────────────────────────────────────
    private async Task<PriceResult> SearchCoinGeckoAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var searchResp = await cryptoHttp.GetAsync($"search?query={symbol}", ct);
            searchResp.EnsureSuccessStatusCode();

            using var searchDoc = JsonDocument.Parse(
                await searchResp.Content.ReadAsStringAsync(ct));

            string? coinId   = null;
            string? coinName = null;

            foreach (var coin in searchDoc.RootElement.GetProperty("coins").EnumerateArray())
            {
                string? sym = coin.TryGetProperty("symbol", out var s) ? s.GetString() : null;
                if (!string.Equals(sym, symbol, StringComparison.OrdinalIgnoreCase)) continue;
                coinId   = coin.TryGetProperty("id",   out var id) ? id.GetString() : null;
                coinName = coin.TryGetProperty("name", out var n)  ? n.GetString() : null;
                break;
            }

            if (coinId is null) return new PriceResult(symbol, "", 0, false);

            var priceResp = await cryptoHttp.GetAsync(
                $"simple/price?ids={coinId}&vs_currencies=usd", ct);
            priceResp.EnsureSuccessStatusCode();

            using var priceDoc = JsonDocument.Parse(
                await priceResp.Content.ReadAsStringAsync(ct));

            double price = priceDoc.RootElement
                .GetProperty(coinId)
                .GetProperty("usd")
                .GetDouble();

            return new PriceResult(symbol, coinName ?? symbol, price, true);
        }
        catch
        {
            return new PriceResult(symbol, "", 0, false);
        }
    }

    // ── Yahoo Finance ─────────────────────────────────────────────
    private async Task<PriceResult> SearchYahooAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var resp = await stockHttp.GetAsync(
                $"v8/finance/chart/{symbol}?interval=1d&range=1d", ct);
            resp.EnsureSuccessStatusCode();

            using var doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var       meta = doc.RootElement
                               .GetProperty("chart")
                               .GetProperty("result")[0]
                               .GetProperty("meta");

            double price = meta.GetProperty("regularMarketPrice").GetDouble();
            string cur   = meta.TryGetProperty("currency", out var c) ? c.GetString() ?? "USD" : "USD";
            string name  = meta.TryGetProperty("shortName", out var n) ? n.GetString() ?? symbol : symbol;

            // Přepočet na USD pokud obchodováno v jiné měně
            double priceUsd = string.Equals(cur, "USD", StringComparison.OrdinalIgnoreCase)
                ? price
                : price * fx.FiatToUsd(cur);

            return new PriceResult(symbol, name, priceUsd, true);
        }
        catch
        {
            return new PriceResult(symbol, "", 0, false);
        }
    }
}
