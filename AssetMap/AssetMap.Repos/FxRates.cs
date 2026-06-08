using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AssetMap.Repos;

/// <summary>
/// Aktuální kurzy fiat měn vůči USD.
/// Zdroj: frankfurter.app (ECB data, zdarma, bez API klíče).
/// Fallback: pevně zakódované hodnoty dokud RefreshAsync() neproběhne.
/// </summary>
public static class FxRates
{
    // Výchozí pevné kurzy (1 USD → X měna)
    private static Dictionary<string, double> _usdTo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CZK"] = 23.2,
        ["EUR"] = 0.9259,
        ["GBP"] = 0.7963,
        ["CHF"] = 0.8796,
        ["JPY"] = 150.0,
        ["PLN"] = 4.08,
    };

    /// <summary>Vyvolá se po úspěšném obnovení kurzů.</summary>
    public static event Action? Updated;

    /// <summary>Převod: 1 USD → X jednotek dané měny.</summary>
    public static double UsdToFiat(string code)
    {
        if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase)) return 1.0;
        return _usdTo.TryGetValue(code, out double v) ? v : 1.0;
    }

    /// <summary>Převod: 1 jednotka dané měny → USD.</summary>
    public static double FiatToUsd(string code)
    {
        if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase)) return 1.0;
        return _usdTo.TryGetValue(code, out double v) && v > 0 ? 1.0 / v : 1.0;
    }

    /// <summary>
    /// Stáhne aktuální kurzy z frankfurter.app a uloží do cache.
    /// Tichá chyba — při selhání zůstanou fallback hodnoty.
    /// </summary>
    public static async Task RefreshAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("User-Agent", "AssetMap/0.1 (portfolio tracker)");

            // Frankfurter: kurzy z USD do všech dostupných měn
            var resp = await http.GetAsync("https://api.frankfurter.app/latest?from=USD");
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var newRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.GetProperty("rates").EnumerateObject())
            {
                if (prop.Value.TryGetDouble(out double rate) && rate > 0)
                    newRates[prop.Name] = rate;
            }

            if (newRates.Count > 0)
            {
                _usdTo = newRates;

                // Aktualizuj dnešní snapshot pro každou měnu
                foreach (var kv in newRates)
                    PriceSnapshotRepo.UpdateLatest(kv.Key, kv.Value > 0 ? 1.0 / kv.Value : 1.0);

                Updated?.Invoke();
            }
        }
        catch
        {
            // Tiché selhání — použijí se fallback kurzy
        }
    }

    /// <summary>
    /// Stáhne denní historii kurzů za posledních <paramref name="days"/> dní a uloží do PriceSnapshotRepo.
    /// Zdroj: frankfurter.app (ECB, pracovní dny; weekendy forward-fillnuty).
    /// </summary>
    public static async Task RefreshHistoryAsync(int days = 365)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.Add("User-Agent", "AssetMap/0.1 (portfolio tracker)");

            string since    = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
            string symbols  = "CZK,EUR,GBP,CHF,PLN,JPY";

            // frankfurter vrací: rates[date][code] = X units per 1 USD
            var resp = await http.GetAsync(
                $"https://api.frankfurter.app/{since}..?from=USD&to={symbols}");
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var ratesRoot = doc.RootElement.GetProperty("rates");

            // Sestavit SortedDictionary per code: date → (1 native → USD) = 1 / usdToNative
            var perCode = new Dictionary<string, SortedDictionary<DateTime, double>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var dayProp in ratesRoot.EnumerateObject())
            {
                if (!DateTime.TryParse(dayProp.Name, out DateTime day)) continue;
                foreach (var codeProp in dayProp.Value.EnumerateObject())
                {
                    double usdToNative = codeProp.Value.GetDouble();
                    if (usdToNative <= 0) continue;
                    double nativeToUsd = 1.0 / usdToNative;

                    if (!perCode.TryGetValue(codeProp.Name, out var dict))
                        perCode[codeProp.Name] = dict = new SortedDictionary<DateTime, double>();
                    dict[day] = nativeToUsd;
                }
            }

            foreach (var kv in perCode)
                PriceSnapshotRepo.StoreHistory(kv.Key, kv.Value, days);
        }
        catch
        {
            // Tiché selhání — AccountRepo.Build použije random walk jako fallback
        }
    }
}
