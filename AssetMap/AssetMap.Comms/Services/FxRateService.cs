using System.Text.Json;

namespace AssetMap.Comms.Services;

/// <summary>
/// Implementace přes frankfurter.app (ECB data, zdarma, bez API klíče).
/// Fallback na pevně zakódované hodnoty při selhání sítě.
/// </summary>
public class FxRateService : IFxRateService
{
    private readonly HttpClient _http;

    private Dictionary<string, double> _usdTo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CZK"] = 23.2,
        ["EUR"] = 0.9259,
        ["GBP"] = 0.7963,
        ["CHF"] = 0.8796,
        ["JPY"] = 150.0,
        ["PLN"] = 4.08,
        ["USD"] = 1.0,
    };

    public FxRateService(HttpClient http)
    {
        _http = http;
        _http.BaseAddress ??= new Uri("https://api.frankfurter.app/");
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "AssetMap/1.0 (portfolio tracker)");
    }

    public double UsdToFiat(string code)
    {
        if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase)) return 1.0;
        return _usdTo.TryGetValue(code, out double v) ? v : 1.0;
    }

    public double FiatToUsd(string code)
    {
        if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase)) return 1.0;
        return _usdTo.TryGetValue(code, out double v) && v > 0 ? 1.0 / v : 1.0;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync("latest?from=USD", ct);
            resp.EnsureSuccessStatusCode();

            using var doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var       next = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                             { ["USD"] = 1.0 };

            foreach (var prop in doc.RootElement.GetProperty("rates").EnumerateObject())
                if (prop.Value.TryGetDouble(out double rate) && rate > 0)
                    next[prop.Name] = rate;

            if (next.Count > 1)
                _usdTo = next;
        }
        catch
        {
            // tiché selhání — použijí se fallback kurzy
        }
    }

    public async Task<Dictionary<string, double[]>> GetHistoryAsync(
        int days = 365, CancellationToken ct = default)
    {
        var result = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string since   = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
            string symbols = "CZK,EUR,GBP,CHF,PLN,JPY";
            var    resp    = await _http.GetAsync($"{since}..?from=USD&to={symbols}", ct);
            resp.EnsureSuccessStatusCode();

            using var doc      = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var       ratesRoot = doc.RootElement.GetProperty("rates");

            // code → SortedDictionary<date, nativeToUsd>
            var perCode = new Dictionary<string, SortedDictionary<DateTime, double>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var dayProp in ratesRoot.EnumerateObject())
            {
                if (!DateTime.TryParse(dayProp.Name, out DateTime day)) continue;
                foreach (var codeProp in dayProp.Value.EnumerateObject())
                {
                    double usdToNative = codeProp.Value.GetDouble();
                    if (usdToNative <= 0) continue;
                    if (!perCode.TryGetValue(codeProp.Name, out var dict))
                        perCode[codeProp.Name] = dict = [];
                    dict[day] = 1.0 / usdToNative; // nativeToUsd
                }
            }

            // Forward-fill weekends/holidays → fixed-length array
            DateTime start = DateTime.UtcNow.Date.AddDays(-days + 1);
            foreach (var (code, dict) in perCode)
            {
                var arr   = new double[days];
                double last = dict.Count > 0 ? dict.First().Value : FiatToUsd(code);
                for (int i = 0; i < days; i++)
                {
                    DateTime d = start.AddDays(i);
                    if (dict.TryGetValue(d, out double v)) last = v;
                    arr[i] = last;
                }
                result[code] = arr;
            }
        }
        catch
        {
            // tiché selhání — vrací prázdný slovník
        }
        return result;
    }
}
