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
    public string  AccountLetter => AccountName.Length > 0 ? AccountName[0].ToString().ToUpper() : "?";
    // Detail data — populated during Reload()
    public Guid?    TransactionId { get; init; }
    public string?  Note          { get; init; }
    public decimal? Fee           { get; init; }
    public string   AssetSymbol   { get; init; } = "";
    public Guid?    FromAccountId { get; init; }
    public Guid?    ToAccountId   { get; init; }
    public bool     IsTransfer    { get; init; }
    public string?  Category      { get; init; }
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
        double    AbsAmount,
        double    AbsConverted   // v display měně — pro statistiky
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
                // Kurz: native → display měna
                double convRate = d.ConvertedBalance.HasValue && d.CurrentBalance > 0
                    ? d.ConvertedBalance.Value / d.CurrentBalance
                    : 1.0;
                var transferBrush = new SolidColorBrush(Color.Parse("#8B5CF6"));
                return d.RecentTransactions.Select(tx =>
                {
                    bool   isTransfer = tx.ToAccountId.HasValue || tx.FromAccountId.HasValue;
                    bool   isCredit   = tx.Type == TransactionType.Deposit;
                    double qty        = (double)tx.Quantity;
                    double converted  = qty * convRate;
                    bool   isCryptoLike2 = d.Account.AccountType is AccountType.CryptoWallet
                                                                  or AccountType.Brokerage;
                    string qtyFmt    = isCryptoLike2
                                       ? AccountCardViewModel.SmartFormatQty(qty)
                                       : AccountCardViewModel.FormatFiat(qty);
                    string amtStr    = (isCredit ? "+" : "−") + qtyFmt + " " + d.BaseCurrency;

                    IBrush iconBrush;
                    string iconText;
                    string desc;

                    if (isTransfer)
                    {
                        iconBrush = transferBrush;
                        iconText  = "⇄";
                        desc      = tx.ToAccountId.HasValue ? "Převod →" : "Převod ←";
                    }
                    else
                    {
                        iconBrush = isCredit ? creditBrush : (IBrush)debitBrush;
                        iconText  = isCredit ? "↓" : "↑";
                        desc      = isCredit ? "Příchozí platba" : "Odchozí platba";
                    }

                    if (!string.IsNullOrWhiteSpace(tx.Note))
                        desc = tx.Note;

                    var item = new TxRowItem(
                        IconText:     iconText,
                        IconBrush:    iconBrush,
                        AccountName:  d.Account.Name,
                        AccountBrush: acctBrush,
                        Description:  desc,
                        Date:         tx.Date.ToString("dd. MM. yyyy"),
                        RawDate:      tx.Date,
                        Amount:       amtStr,
                        IsCredit:     isCredit
                    )
                    {
                        TransactionId = tx.Id,
                        Note          = tx.Note,
                        Fee           = tx.Fee,
                        AssetSymbol   = d.BaseCurrency,
                        FromAccountId = tx.FromAccountId,
                        ToAccountId   = tx.ToAccountId,
                        IsTransfer    = isTransfer,
                        Category      = tx.Category,
                    };

                    return new RawRow(item, d.Account.Name, isCredit, tx.Date, qty, converted);
                });
            })
            .OrderByDescending(r => r.Date)
            .ToList();

        ApplyFilter();
    }

    // ── Detail transakce (otevírá modal v AccountsVM) ────────────────────
    [RelayCommand]
    private void OpenTxDetail(TxRowItem row)
    {
        // Rebuild as TransactionDisplayItem so AccountsVM modal can display it
        var detail = new TransactionDisplayItem(
            row.IconText, row.IconBrush, row.Description,
            row.Date, row.Amount, row.IsCredit)
        {
            TransactionId = row.TransactionId ?? Guid.Empty,
            Note          = row.Note,
            Fee           = row.Fee,
            AssetSymbol   = row.AssetSymbol,
            AccountName   = row.AccountName,
            FromAccountId = row.FromAccountId,
            ToAccountId   = row.ToAccountId,
            IsTransfer    = row.IsTransfer,
            RawDateTime   = row.RawDate,
            Category      = row.Category,
        };
        AccountsViewModel.OpenTxDetailRequest?.Invoke(detail);
    }

    // ── Přidat transakci — dialog ────────────────────────────
    [ObservableProperty] private bool _isTxDialogOpen = false;

    // Účty pro ComboBox (jen jména; ID paralelně v _txAccountIds)
    public ObservableCollection<string> TxAccountOptions { get; } = [];
    private readonly List<Guid> _txAccountIds = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxCanConfirm))]
    private int _txAccountIndex = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxIsWithdrawal))]
    [NotifyPropertyChangedFor(nameof(TxCanConfirm))]
    private bool _txIsDeposit = true;
    public bool TxIsWithdrawal => !TxIsDeposit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxCanConfirm))]
    private string _txAmountText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxCanConfirm))]
    private string _txDateText = "";

    [ObservableProperty] private string _txNoteText = "";

    public bool TxCanConfirm =>
        TxAccountIndex >= 0 && TxAccountIndex < _txAccountIds.Count &&
        double.TryParse(TxAmountText.Replace(',', '.'), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out double v) && v > 0 &&
        DateTime.TryParse(TxDateText, out _);

    [RelayCommand] private void SetTxDeposit()    => TxIsDeposit = true;
    [RelayCommand] private void SetTxWithdrawal() => TxIsDeposit = false;

    [RelayCommand] private void OpenAddTransaction()
    {
        // Deleguje na AccountsVM dialog (nastaveno v MainViewModel)
        AccountsViewModel.OpenTxDialogRequest?.Invoke();
    }

    [RelayCommand] private void CancelTx() => IsTxDialogOpen = false;

    [RelayCommand] private void ConfirmTx()
    {
        if (!TxCanConfirm) return;

        double.TryParse(TxAmountText.Replace(',', '.'), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out double amount);
        DateTime.TryParse(TxDateText, out DateTime date);

        AccountRepo.AddTransaction(
            accountId: _txAccountIds[TxAccountIndex],
            type:      TxIsDeposit ? TransactionType.Deposit : TransactionType.Withdrawal,
            amount:    amount,
            date:      date,
            note:      string.IsNullOrWhiteSpace(TxNoteText) ? null : TxNoteText
        );

        IsTxDialogOpen = false;
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

        string cur = AccountRepo.DisplayCurrency;
        TotalIn  = "+" + statsBase.Where(r =>  r.IsCredit).Sum(r => r.AbsConverted)
                             .ToString("N0", CultureInfo.CurrentCulture) + " " + cur;
        TotalOut = "−" + statsBase.Where(r => !r.IsCredit).Sum(r => r.AbsConverted)
                             .ToString("N0", CultureInfo.CurrentCulture) + " " + cur;
        Count    = filtered.Count;
    }
}
