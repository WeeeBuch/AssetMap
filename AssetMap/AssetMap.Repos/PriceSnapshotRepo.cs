using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetMap.Repos;

/// <summary>
/// In-memory sklad denních kurzů aktiv (1 native unit → USD).
/// Aligned na posledních N dní (index 0 = nejstarší, index N-1 = dnes).
/// Weekendy / svátky: forward-fill z posledního dostupného dne.
/// TODO: nahradit perzistentním uložením (SQLite / API).
/// </summary>
public static class PriceSnapshotRepo
{
    // currencyCode (velká písmena) → double[days]  (1 native → USD)
    private static readonly Dictionary<string, double[]> _daily =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Uloží historii kurzů. Výstupní pole bude aligned na <paramref name="days"/> dnů
    /// od dneška dozadu (weekendy / díry forward-fillnuty).
    /// </summary>
    /// <param name="code">ISO kód měny, např. "CZK"</param>
    /// <param name="dateToUsdRate">Datum → kurz (1 unit → USD)</param>
    /// <param name="days">Počet dní výstupního pole</param>
    public static void StoreHistory(
        string code,
        SortedDictionary<DateTime, double> dateToUsdRate,
        int days = 365)
    {
        var aligned = new double[days];
        var today   = DateTime.UtcNow.Date;
        double last = dateToUsdRate.Count > 0 ? dateToUsdRate.First().Value : 1.0;

        for (int i = 0; i < days; i++)
        {
            var day = today.AddDays(i - days + 1);
            // Najdi nejnovější záznam ≤ day
            var entry = dateToUsdRate.LastOrDefault(kv => kv.Key.Date <= day);
            if (entry.Key != default && entry.Value > 0)
                last = entry.Value;
            aligned[i] = last;
        }

        _daily[code.ToUpperInvariant()] = aligned;
    }

    /// <summary>
    /// Aktualizuj dnešní (poslední) hodnotu (voláno po každém FxRates.RefreshAsync).
    /// </summary>
    public static void UpdateLatest(string code, double usdRate)
    {
        code = code.ToUpperInvariant();
        if (_daily.TryGetValue(code, out var arr) && arr.Length > 0)
            arr[^1] = usdRate;
    }

    /// <summary>Vrátí aligned pole kurzů, nebo false pokud data nejsou dostupná.</summary>
    public static bool TryGetHistory(string code, out double[] history)
        => _daily.TryGetValue(code.ToUpperInvariant(), out history!);

    /// <summary>True pokud máme historii pro daný kód.</summary>
    public static bool HasHistory(string code)
        => _daily.ContainsKey(code.ToUpperInvariant());
}
