using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AssetMap.Entities.Enums;
using AssetMap.Repos.Accounts;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetMap.Avalonia.ViewModels;

// ── Model pro řádek transakce v seznamu ───────────────────────
public record TransactionDisplayItem(
    string IconText,
    IBrush IconBrush,
    string Description,
    string Date,
    string Amount,
    bool   IsCredit          // true = zelená, false = červená
);

// ── Karta účtu ────────────────────────────────────────────────
public partial class AccountCardViewModel : ViewModelBase
{
    // Zobrazovací data
    public string AccountName  { get; init; } = "";
    public string Institution  { get; init; } = "";
    public string IconLetter   { get; init; } = "?";
    public IBrush IconBrush    { get; init; } = Brushes.Gray;

    public string  BaseAmount        { get; init; } = "0,00";
    public string  BaseCurrency      { get; init; } = "";
    public string? ConvertedAmount   { get; set;  }
    public string? ConvertedCurrency { get; set;  }
    public string  ChangeText        { get; init; } = "";
    public bool    ChangePositive    { get; init; }

    // Surové hodnoty pro výpočty (přehledové grafy)
    public double RawBalance     { get; init; }
    public double ConversionRate { get; init; } = 1.0;

    // Výběr — nastavuje AccountsViewModel
    [ObservableProperty] private bool _isSelected;

    internal Action<AccountCardViewModel>? OnSelect { get; set; }

    [RelayCommand]
    private void Select() => OnSelect?.Invoke(this);

    // Graf — jednoduché pole hodnot (bez externích závislostí)
    public double[] BalanceHistory    { get; init; } = [];
    public int[]    DepositIndices    { get; init; } = [];
    public int[]    WithdrawalIndices { get; init; } = [];

    // Transakce (seznam pod grafem)
    public ObservableCollection<TransactionDisplayItem> Transactions { get; init; } = [];

    // ── Real data factory ─────────────────────────────────────
    public static AccountCardViewModel FromData(AccountData data)
    {
        var acc      = data.Account;
        var history  = data.BalanceHistory;
        var iconBrush = BrushFor(acc.AccountType, acc.Name);

        // Konverzní kurz = ConvertedBalance / CurrentBalance (např. CZK→EUR)
        double conversionRate = data.ConvertedBalance.HasValue && data.CurrentBalance > 0
            ? data.ConvertedBalance.Value / data.CurrentBalance
            : 1.0;

        // Deposit / withdrawal indices z dat transakcí
        var baseDate = DateTime.Today.AddDays(-(history.Length - 1));
        var deposits    = new List<int>();
        var withdrawals = new List<int>();

        foreach (var tx in data.RecentTransactions)
        {
            int idx = (int)(tx.Date.Date - baseDate).TotalDays;
            if (idx < 0 || idx >= history.Length) continue;
            if (tx.Type == TransactionType.Deposit)    deposits.Add(idx);
            else if (tx.Type == TransactionType.Withdrawal) withdrawals.Add(idx);
        }

        // Zobrazovací seznam transakcí
        var creditBrush = new SolidColorBrush(Color.Parse("#00E57A"));
        var debitBrush  = new SolidColorBrush(Color.Parse("#FF3355"));

        var txItems = data.RecentTransactions
            .Select(tx =>
            {
                bool isCredit = tx.Type == TransactionType.Deposit;
                return new TransactionDisplayItem(
                    isCredit ? "↓" : "↑",
                    isCredit ? creditBrush : debitBrush,
                    isCredit ? "Příchozí platba" : "Odchozí platba",
                    tx.Date.ToString("dd. MM. yyyy"),
                    (isCredit ? "+" : "−") + ((double)tx.Quantity).ToString("N2") + " " + data.BaseCurrency,
                    isCredit
                );
            })
            .ToList();

        double finalBalance = history.Length > 0 ? history[^1] : data.CurrentBalance;
        double firstBalance = history.Length > 0 ? history[0]  : finalBalance;
        double pct = firstBalance > 0 ? (finalBalance - firstBalance) / firstBalance * 100 : 0;

        return new AccountCardViewModel
        {
            AccountName       = acc.Name,
            Institution       = acc.Institution ?? "",
            IconLetter        = acc.Name.Length > 0 ? acc.Name[0].ToString().ToUpper() : "?",
            IconBrush         = iconBrush,
            BaseAmount        = finalBalance.ToString("N2"),
            BaseCurrency      = data.BaseCurrency,
            ConvertedAmount   = data.ConvertedBalance.HasValue
                                    ? data.ConvertedBalance.Value.ToString("N2") : null,
            ConvertedCurrency = data.ConvertedCurrency,
            ChangeText        = (pct >= 0 ? "▲ +" : "▼ ") + Math.Abs(pct).ToString("N1") + " %",
            ChangePositive    = pct >= 0,
            RawBalance        = finalBalance,
            ConversionRate    = conversionRate,
            BalanceHistory    = history,
            DepositIndices    = [.. deposits],
            WithdrawalIndices = [.. withdrawals],
            Transactions      = new ObservableCollection<TransactionDisplayItem>(txItems),
        };
    }

    /// <summary>Deterministická barva ikony podle typu/jména.</summary>
    public static IBrush BrushFor(AccountType type, string name) => type switch
    {
        AccountType.CryptoWallet => new SolidColorBrush(Color.Parse("#F7931A")), // BTC oranžová
        AccountType.Brokerage    => new SolidColorBrush(Color.Parse("#6C5CE7")), // fialová
        _ => PaletteFor(name)
    };

    private static readonly string[] _palette =
    [
        "#00B3FF", "#00E57A", "#845EF7", "#FF922B",
        "#F03E3E", "#12B886", "#4263EB", "#F59F00"
    ];
    private static IBrush PaletteFor(string name)
    {
        int idx = Math.Abs(name.GetHashCode()) % _palette.Length;
        return new SolidColorBrush(Color.Parse(_palette[idx]));
    }

    // ── Mock data factory (dočasně pro dev/test) ──────────────
    public static AccountCardViewModel CreateMock(
        string name, string institution, string iconLetter, string iconColorHex,
        double startBalance, string baseCurrency,
        string convertedCurrency, double conversionRate,
        int seed)
    {
        var rng = new Random(seed);
        var now = DateTime.Now.Date;

        var balanceHistory  = new double[60];
        var depositIndices  = new List<int>();
        var withdrawIndices = new List<int>();
        var transactions    = new List<TransactionDisplayItem>();

        var creditBrush = new SolidColorBrush(Color.Parse("#00E57A"));
        var debitBrush  = new SolidColorBrush(Color.Parse("#FF3355"));

        double balance = startBalance;

        for (int i = 0; i < 60; i++)
        {
            var date  = now.AddDays(i - 59);
            double delta = (rng.NextDouble() - 0.44) * startBalance * 0.025;
            balance = Math.Max(balance + delta, startBalance * 0.1);
            balanceHistory[i] = balance;

            // ~15 % šance na transakci
            if (rng.NextDouble() < 0.15)
            {
                bool isCredit = rng.NextDouble() > 0.45;
                double amount = startBalance * (0.01 + rng.NextDouble() * 0.06);

                if (isCredit) depositIndices.Add(i);
                else          withdrawIndices.Add(i);

                transactions.Insert(0, new TransactionDisplayItem(
                    isCredit ? "↓" : "↑",
                    isCredit ? creditBrush : debitBrush,
                    isCredit ? "Příchozí platba" : "Odchozí platba",
                    date.ToString("dd. MM. yyyy"),
                    (isCredit ? "+" : "−") + amount.ToString("N2") + " " + baseCurrency,
                    isCredit
                ));
            }
        }

        double finalBalance = balanceHistory[^1];
        double firstBalance = balanceHistory[0];
        double pct = (finalBalance - firstBalance) / firstBalance * 100;

        return new AccountCardViewModel
        {
            AccountName       = name,
            Institution       = institution,
            IconLetter        = iconLetter,
            IconBrush         = new SolidColorBrush(Color.Parse(iconColorHex)),
            BaseAmount        = finalBalance.ToString("N2"),
            BaseCurrency      = baseCurrency,
            ConvertedAmount   = (finalBalance * conversionRate).ToString("N2"),
            ConvertedCurrency = convertedCurrency,
            ChangeText        = (pct >= 0 ? "▲ +" : "▼ ") + Math.Abs(pct).ToString("N1") + " %",
            ChangePositive    = pct >= 0,
            RawBalance        = finalBalance,
            ConversionRate    = conversionRate,
            BalanceHistory    = balanceHistory,
            DepositIndices    = [.. depositIndices],
            WithdrawalIndices = [.. withdrawIndices],
            Transactions      = new ObservableCollection<TransactionDisplayItem>(transactions.Take(25)),
        };
    }
}
