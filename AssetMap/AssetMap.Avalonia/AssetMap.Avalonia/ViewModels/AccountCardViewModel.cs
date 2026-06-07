using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    // ── Mock data factory ─────────────────────────────────────
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
            BalanceHistory    = balanceHistory,
            DepositIndices    = [.. depositIndices],
            WithdrawalIndices = [.. withdrawIndices],
            Transactions      = new ObservableCollection<TransactionDisplayItem>(transactions.Take(25)),
        };
    }
}
