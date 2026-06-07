using AssetMap.Entities;

namespace AssetMap.Repos.Accounts;

/// <summary>
/// Předpočítaná data účtu připravená pro UI.
/// Server/API je zodpovědný za přepočty měn a historii hodnot.
/// </summary>
public record AccountData
{
    public required Account  Account           { get; init; }

    // Primární zůstatek v domácí měně účtu (např. "42 500" + "Kč")
    public required double   CurrentBalance    { get; init; }
    public required string   BaseCurrency      { get; init; }

    // Volitelný přepočet do zobrazené měny (EUR, USD…)
    public          double?  ConvertedBalance  { get; init; }
    public          string?  ConvertedCurrency { get; init; }

    // 60 denních hodnot vzestupně (index 0 = nejstarší)
    public required double[] BalanceHistory    { get; init; }

    // Posledních N transakcí sestupně (pro seznam + tečky v grafu)
    public required IReadOnlyList<Transaction> RecentTransactions { get; init; }
}
