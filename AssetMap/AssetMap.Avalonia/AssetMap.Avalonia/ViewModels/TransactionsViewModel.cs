using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using AssetMap.Entities.Enums;
using AssetMap.Repos.Accounts;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetMap.Avalonia.ViewModels;

// ── Řádek v tabulce transakcí ─────────────────────────────────
public record TxRowItem(
    string   IconText,
    IBrush   IconBrush,
    string   AccountName,
    IBrush   AccountBrush,
    string   Description,
    string   Date,
    DateTime RawDate,
    string   Amount,
    bool     IsCredit
)
{
    public string AccountLetter =>
        AccountName.Length > 0 ? AccountName[0].ToString().ToUpper() : "?";
}

// ── ViewModel pro stránku Transakcí ──────────────────────────
public partial class TransactionsViewModel : ViewModelBase
{
    // ── Interní syrový řádek ──────────────────────────────────
    private record RawRow(
        TxRowItem Item,
        string    AccountName,
        bool      IsCredit,
        DateTime  Date,
        double    AbsAmount
    );

    private List<RawRow> _all = [];

    // ── Filtrovaný výstup pro ListView ────────────────────────
    public ObservableCollection<TxRowItem> Filtered { get; } = [];

    // ── Filtr: účet ───────────────────────────────────────────
    public ObservableCollection<string> AccountOptions { get; } = ["Všechny účty"];

    [ObservableProperty] private int _selectedAccountIndex = 0;
    partial void OnSelectedAccountIndexChanged(int value) => ApplyFilter();

    // ── Filtr: typ transakce ──────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTypeAll))]
    [NotifyPropertyChangedFor(nameof(IsTypeCredit))]
    [NotifyPropertyChangedFor(nameof(IsTypeDebit))]
    private int _typeFilter = 0;   // 0 = vše, 1 = příchozí, 2 = odchozí

    partial void OnTypeFilterChanged(int value) => ApplyFilter();

    public bool IsTypeAll    => TypeFilter == 0;
    public bool IsTypeCredit => TypeFilter == 1;
    public bool IsTypeDebit  => TypeFilter == 2;

    [RelayCommand] private void FilterAll()    => TypeFilter = 0;
    [RelayCommand] private void FilterCredit() => TypeFilter = 1;
    [RelayCommand] private void FilterDebit()  => TypeFilter = 2;

    // ── Statistiky ────────────────────────────────────────────
    private string _totalIn  = "0";
    private string _totalOut = "0";
    private int    _count    = 0;

    public string TotalIn  { get => _totalIn;  private set { _totalIn  = value; OnPropertyChanged(); } }
    public string TotalOut { get => _totalOut; private set { _totalOut = value; OnPropertyChanged(); } }
    public int    Count    { get => _count;    private set { _count    = value; OnPropertyChanged(); } }

    // ── Konstruktor ───────────────────────────────────────────
    public TransactionsViewModel()
    {
        AccountRepo.DataRefreshed += () => Dispatcher.UIThread.Post(Reload);
        Reload();
    }

    // ── Načtení dat ze cache ──────────────────────────────────
    private void Reload()
    {
        var data = AccountRepo.GetAll();

        // Aktualizace možností filtru účtu
        AccountOptions.Clear();
        AccountOptions.Add("Všechny účty");
        foreach (var d in data)
            AccountOptions.Add(d.Account.Name);

        if (SelectedAccountIndex >= AccountOptions.Count)
            SelectedAccountIndex = 0;

        var creditBrush = new SolidColorBrush(Color.Parse("#00E57A"));
        var debitBrush  = new SolidColorBrush(Color.Parse("#FF3355"));

        _all = data
            .SelectMany(d =>
            {
                var acctBrush = AccountCardViewModel.BrushFor(d.Account.AccountType, d.Account.Name);
                return d.RecentTransactions.Select(tx =>
                {
                    bool   isCredit = tx.Type == TransactionType.Deposit;
                    double qty      = (double)tx.Quantity;
                    string amtStr   = (isCredit ? "+" : "−")
                                      + qty.ToString("N2", CultureInfo.CurrentCulture)
                                      + " " + d.BaseCurrency;

                    var item = new TxRowItem(
                        IconText:    isCredit ? "↓" : "↑",
                        IconBrush:   isCredit ? creditBrush : (IBrush)debitBrush,
                        AccountName: d.Account.Name,
                        AccountBrush: acctBrush,
                        Description: isCredit ? "Příchozí platba" : "Odchozí platba",
                        Date:        tx.Date.ToString("dd. MM. yyyy"),
                        RawDate:     tx.Date,
                        Amount:      amtStr,
                        IsCredit:    isCredit
                    );

                    return new RawRow(item, d.Account.Name, isCredit, tx.Date, qty);
                });
            })
            .OrderByDescending(r => r.Date)
            .ToList();

        ApplyFilter();
    }

    // ── Aplikace filtrů ───────────────────────────────────────
    private void ApplyFilter()
    {
        string? acctFilter = SelectedAccountIndex > 0 && SelectedAccountIndex < AccountOptions.Count
            ? AccountOptions[SelectedAccountIndex]
            : null;

        var filtered = _all
            .Where(r => acctFilter == null || r.AccountName == acctFilter)
            .Where(r => TypeFilter == 0
                     || (TypeFilter == 1 &&  r.IsCredit)
                     || (TypeFilter == 2 && !r.IsCredit))
            .ToList();

        Filtered.Clear();
        foreach (var r in filtered)
            Filtered.Add(r.Item);

        // Statistiky: příchozí/odchozí z vybraného účtu (bez filtru typu)
        var statsBase = acctFilter == null
            ? _all
            : _all.Where(r => r.AccountName == acctFilter).ToList();

        TotalIn  = "+" + statsBase.Where(r =>  r.IsCredit).Sum(r => r.AbsAmount)
                             .ToString("N0", CultureInfo.CurrentCulture);
        TotalOut = "−" + statsBase.Where(r => !r.IsCredit).Sum(r => r.AbsAmount)
                             .ToString("N0", CultureInfo.CurrentCulture);
        Count    = filtered.Count;
    }
}
