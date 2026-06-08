namespace AssetMap.Comms.Services;

/// <summary>
/// Aktuální a historické kurzy fiat měn vůči USD.
/// </summary>
public interface IFxRateService
{
    /// <summary>1 USD → X jednotek dané měny.</summary>
    double UsdToFiat(string code);

    /// <summary>1 jednotka dané měny → USD.</summary>
    double FiatToUsd(string code);

    /// <summary>Stáhne aktuální kurzy z frankfurter.app (ECB). Tichá chyba → fallback.</summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Stáhne denní historii kurzů za posledních <paramref name="days"/> dní.
    /// Vrací: code → pole hodnot (native → USD), délka = days, nejstarší první.
    /// </summary>
    Task<Dictionary<string, double[]>> GetHistoryAsync(int days = 365, CancellationToken ct = default);
}
