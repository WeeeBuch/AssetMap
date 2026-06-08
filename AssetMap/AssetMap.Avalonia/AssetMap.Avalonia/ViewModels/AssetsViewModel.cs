using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using AssetMap.Entities.Enums;
using AssetMap.Repos.Accounts;
using Avalonia.Media;
using Avalonia.Threading;

namespace AssetMap.Avalonia.ViewModels;

// ── Řádek aktiva ──────────────────────────────────────────────
public record AssetRowItem(
    string   Symbol,
    string   Name,
    string   TypeLabel,
    IBrush   TypeBrush,
    IBrush   TypeBgBrush,
    IBrush   IconBrush,
    string   QtyText,
    string   ValueText,
    double   AllocPct,
    string   AllocText,
    double   BarWidthPx      // max 140 px
);

// ── Typ-level řádek pro přehled nahoře ────────────────────────
public record AssetTypeRow(
    string   TypeLabel,
    IBrush   Brush,
    string   ValueText,
    string   AllocText
);

// ── ViewModel ─────────────────────────────────────────────────
public partial class AssetsViewModel : ViewModelBase
{
    // Pie (rozdělení podle typu: Fiat / Crypto / ...)
    private PieSliceData[] _pieSlices = [];
    public PieSliceData[] PieSlices
    {
        get => _pieSlices;
        private set { _pieSlices = value; OnPropertyChanged(); }
    }

    // Přehled typů (pod koláčem)
    public ObservableCollection<AssetTypeRow> TypeRows { get; } = [];

    // Detailní seznam aktiv
    public ObservableCollection<AssetRowItem> Assets { get; } = [];

    // Celková hodnota + počet
    private string _totalText = "0";
    private int    _assetCount;

    public string TotalText
    {
        get => _totalText;
        private set { _totalText = value; OnPropertyChanged(); }
    }
    public int AssetCount
    {
        get => _assetCount;
        private set { _assetCount = value; OnPropertyChanged(); }
    }

    // ── Init ──────────────────────────────────────────────────
    public AssetsViewModel()
    {
        AccountRepo.DataRefreshed += () => Dispatcher.UIThread.Post(Reload);
        Reload();
    }

    // ── Načtení ───────────────────────────────────────────────
    private void Reload()
    {
        var data = AccountRepo.GetAll();

        // Aggregate by symbol
        var grouped = data
            .GroupBy(d => d.BaseCurrency)
            .Select(g =>
            {
                var first     = g.First();
                double qty    = g.Sum(d => d.CurrentBalance);
                double eur    = g.Sum(d => d.ConvertedBalance ?? d.CurrentBalance);
                var assetType = AssetTypeFrom(first.Account.AccountType);
                return (Symbol: g.Key, Qty: qty, Eur: eur, Type: assetType);
            })
            .OrderByDescending(a => a.Eur)
            .ToList();

        string cur      = AccountRepo.DisplayCurrency;
        double totalEur = grouped.Sum(a => a.Eur);
        TotalText  = Fmt(totalEur) + " " + cur;
        AssetCount = grouped.Count;

        // Pie — podle TYPU (Fiat / Crypto / Stock)
        var byType = grouped
            .GroupBy(a => a.Type)
            .Select(g => (Type: g.Key, Eur: g.Sum(a => a.Eur)))
            .OrderByDescending(t => t.Eur)
            .ToList();

        PieSlices = byType.Select(t => new PieSliceData
        {
            Label      = TypeLabel(t.Type),
            Value      = t.Eur,
            Percent    = totalEur > 0
                ? (t.Eur / totalEur * 100).ToString("N1", CultureInfo.CurrentCulture) + " %"
                : "0 %",
            SliceBrush = TypeBrush(t.Type),
        }).ToArray();

        TypeRows.Clear();
        foreach (var t in byType)
        {
            double pct = totalEur > 0 ? t.Eur / totalEur * 100 : 0;
            TypeRows.Add(new AssetTypeRow(
                TypeLabel:  TypeLabel(t.Type),
                Brush:      TypeBrush(t.Type),
                ValueText:  Fmt(t.Eur) + " " + cur,
                AllocText:  pct.ToString("N1", CultureInfo.CurrentCulture) + " %"
            ));
        }

        // Detailní seznam aktiv
        Assets.Clear();
        foreach (var a in grouped)
        {
            double pct       = totalEur > 0 ? a.Eur / totalEur * 100 : 0;
            var    iconBrush = AssetBrush(a.Symbol, a.Type);
            var    tBrush    = TypeBrush(a.Type);
            var    tBgBrush  = TypeBgBrush(a.Type);

            Assets.Add(new AssetRowItem(
                Symbol:     a.Symbol,
                Name:       AssetName(a.Symbol),
                TypeLabel:  TypeLabel(a.Type),
                TypeBrush:  tBrush,
                TypeBgBrush: tBgBrush,
                IconBrush:  iconBrush,
                QtyText:    FmtQty(a.Qty, a.Symbol),
                ValueText:  Fmt(a.Eur) + " " + cur,
                AllocPct:   pct,
                AllocText:  pct.ToString("N1", CultureInfo.CurrentCulture) + " %",
                BarWidthPx: pct / 100.0 * 140.0
            ));
        }
    }

    // ── Helpers ───────────────────────────────────────────────
    private static AssetType AssetTypeFrom(AccountType t) => t switch
    {
        AccountType.CryptoWallet => AssetType.Crypto,
        AccountType.Brokerage    => AssetType.Stock,
        _                        => AssetType.Fiat,
    };

    private static string TypeLabel(AssetType t) => t switch
    {
        AssetType.Crypto => "Crypto",
        AssetType.Stock  => "Akcie",
        AssetType.Fiat   => "Fiat",
        _                => "Jiné",
    };

    private static IBrush TypeBrush(AssetType t) => t switch
    {
        AssetType.Crypto => new SolidColorBrush(Color.Parse("#F7931A")),
        AssetType.Stock  => new SolidColorBrush(Color.Parse("#845EF7")),
        _                => new SolidColorBrush(Color.Parse("#00B3FF")),
    };

    private static IBrush TypeBgBrush(AssetType t) => t switch
    {
        AssetType.Crypto => new SolidColorBrush(Color.Parse("#28F7931A")),
        AssetType.Stock  => new SolidColorBrush(Color.Parse("#28845EF7")),
        _                => new SolidColorBrush(Color.Parse("#2800B3FF")),
    };

    private static IBrush AssetBrush(string symbol, AssetType type) => symbol switch
    {
        "BTC" => new SolidColorBrush(Color.Parse("#F7931A")),
        "ETH" => new SolidColorBrush(Color.Parse("#627EEA")),
        "SOL" => new SolidColorBrush(Color.Parse("#9945FF")),
        "EUR" => new SolidColorBrush(Color.Parse("#00E57A")),
        "USD" => new SolidColorBrush(Color.Parse("#00B3FF")),
        "Kč"  => new SolidColorBrush(Color.Parse("#4263EB")),
        _ when type == AssetType.Crypto => new SolidColorBrush(Color.Parse("#845EF7")),
        _ when type == AssetType.Stock  => new SolidColorBrush(Color.Parse("#845EF7")),
        _                               => new SolidColorBrush(Color.Parse("#6A6A82")),
    };

    private static string AssetName(string symbol) => symbol switch
    {
        "Kč"  => "Česká koruna",
        "EUR" => "Euro",
        "USD" => "Americký dolar",
        "GBP" => "Britská libra",
        "BTC" => "Bitcoin",
        "ETH" => "Ethereum",
        "SOL" => "Solana",
        "XRP" => "XRP",
        _     => symbol,
    };

    private static string FmtQty(double qty, string symbol)
    {
        bool isCrypto = symbol is "BTC" or "ETH" or "SOL" or "XRP";
        return isCrypto
            ? qty.ToString("N4", CultureInfo.CurrentCulture) + " " + symbol
            : ((long)Math.Round(qty)).ToString("N0", CultureInfo.CurrentCulture) + " " + symbol;
    }

    private static string Fmt(double v) =>
        ((long)Math.Round(v)).ToString("N0", CultureInfo.CurrentCulture);
}
