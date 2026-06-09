using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetMap.Entities.Enums;
using AssetMap.Repos;
using AssetMap.Repos.Accounts;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetMap.Avalonia.ViewModels;

public enum AssetSearchStatus { None, Searching, Found, NotFound, Error, Manual }

public partial class AccountsViewModel : ViewModelBase
{
    // ── Kolekce účtů ─────────────────────────────────────────
    public ObservableCollection<AccountCardViewModel> Accounts { get; } = [];

    // ── Vybraný účet ─────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAccount))]
    private AccountCardViewModel? _selectedAccount;

    public bool HasSelectedAccount => SelectedAccount is not null;

    // ── Import CSV stav ───────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportStatusOk))]
    [NotifyPropertyChangedFor(nameof(ImportStatusError))]
    private string? _importStatusText;

    public bool ImportStatusOk    => ImportStatusText is not null && !ImportStatusText.StartsWith("✗");
    public bool ImportStatusError => ImportStatusText is not null &&  ImportStatusText.StartsWith("✗");

    [ObservableProperty] private bool _isImporting;

    public async Task RunImportAsync(Guid accountId, string fileName, byte[] csvBytes)
    {
        IsImporting      = true;
        ImportStatusText = null;
        try
        {
            var result = await AccountRepo.ImportCsvAsync(accountId, fileName, csvBytes);
            if (result is null)
            {
                ImportStatusText = "✗ Server nedostupný — import selhal";
                return;
            }
            if (!result.Success)
            {
                ImportStatusText = $"✗ {result.ErrorMessage}";
                return;
            }
            ImportStatusText = result.ErrorCount == 0
                ? $"✓ Importováno {result.SuccessCount} transakcí ({result.DetectedFormat})"
                : $"✓ {result.SuccessCount} OK, {result.ErrorCount} chyb ({result.DetectedFormat})";
        }
        catch (Exception ex)
        {
            ImportStatusText = $"✗ {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    // ── Editace poznámky transakce ────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingTransaction))]
    private Guid _editingTransactionId;

    [ObservableProperty] private string _editTransactionNote = "";

    public bool IsEditingTransaction => EditingTransactionId != Guid.Empty;

    [RelayCommand]
    private void StartEditTransaction(TransactionDisplayItem tx)
    {
        EditingTransactionId = tx.TransactionId;
        EditTransactionNote  = tx.Note ?? tx.Description;
    }

    [RelayCommand]
    private void CancelEditTransaction()
    {
        EditingTransactionId = Guid.Empty;
        EditTransactionNote  = "";
    }

    [RelayCommand]
    private async Task SaveTransactionNoteAsync()
    {
        if (EditingTransactionId == Guid.Empty) return;
        var id   = EditingTransactionId;
        var note = EditTransactionNote.Trim();

        EditingTransactionId = Guid.Empty;
        EditTransactionNote  = "";

        bool ok = await AccountRepo.UpdateTransactionNoteAsync(id, note);
        if (ok)
            await AccountRepo.RefreshAsync();   // reload so Description updates
    }

    // ── Detail transakce — MODAL ─────────────────────────────────

    /// <summary>Otevření detailu transakce z jiných VM (TransactionsVM).</summary>
    internal static Action<TransactionDisplayItem>? OpenTxDetailRequest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TxDetailFromAccountName))]
    [NotifyPropertyChangedFor(nameof(TxDetailToAccountName))]
    [NotifyPropertyChangedFor(nameof(TxDetailFee))]
    [NotifyPropertyChangedFor(nameof(TxDetailNote))]
    [NotifyPropertyChangedFor(nameof(TxDetailDate))]
    [NotifyPropertyChangedFor(nameof(TxDetailAccountName))]
    [NotifyPropertyChangedFor(nameof(TxDetailTypeLabel))]
    [NotifyPropertyChangedFor(nameof(TxDetailCategory))]
    private TransactionDisplayItem? _selectedTxDetail;

    [ObservableProperty] private bool _isTxDetailOpen = false;

    public string TxDetailFromAccountName =>
        SelectedTxDetail?.FromAccountId is Guid fid
        ? Accounts.FirstOrDefault(a => a.AccountId == fid)?.AccountName ?? "?"
        : "–";

    public string TxDetailToAccountName =>
        SelectedTxDetail?.ToAccountId is Guid tid
        ? Accounts.FirstOrDefault(a => a.AccountId == tid)?.AccountName ?? "?"
        : "–";

    public string TxDetailAccountName => SelectedTxDetail?.AccountName ?? "–";
    public string TxDetailNote        => string.IsNullOrWhiteSpace(SelectedTxDetail?.Note) ? "–" : SelectedTxDetail.Note;
    public string TxDetailDate        => SelectedTxDetail?.RawDateTime.ToString("d. MMMM yyyy") ?? "–";
    public string TxDetailFee         =>
        SelectedTxDetail?.Fee is decimal fee && fee > 0
        ? fee.ToString("N2") + " " + (SelectedTxDetail.AssetSymbol ?? "")
        : "–";
    public string TxDetailTypeLabel =>
        SelectedTxDetail is { } d
            ? d.IsTransfer ? "Převod" : (d.IsCredit ? "Příjem" : "Výdaj")
            : "–";
    public string TxDetailCategory =>
        string.IsNullOrWhiteSpace(SelectedTxDetail?.Category) ? "–" : SelectedTxDetail.Category;

    [RelayCommand]
    private void OpenTxDetail(TransactionDisplayItem tx)
    {
        SelectedTxDetail = tx;
        IsTxDetailOpen   = true;
    }

    [RelayCommand]
    private void CloseTxDetail() => IsTxDetailOpen = false;

    // ── Nová manuální transakce — DIALOG ─────────────────────────

    /// <summary>Otevření dialogu přidat transakci z jiných VM (TransactionsVM).</summary>
    internal static Action? OpenTxDialogRequest;

    // Dialog open/close
    [ObservableProperty] private bool _isNewTxDialogOpen = false;

    [RelayCommand]
    private void OpenNewTxDialog()
    {
        NewTxDate             = DateTimeOffset.Now;
        NewTxAmount           = "";
        NewTxFee              = "";
        NewTxNote             = "";
        NewTxCategory         = "";
        NewTxTransferTarget   = null;
        NewTxMode             = NewTxMode.In;
        // Defaultní zdrojový účet = aktuálně vybraný (nebo první v listu)
        NewTxSourceAccount    = SelectedAccount ?? Accounts.FirstOrDefault();
        IsNewTxDialogOpen     = true;
    }

    [RelayCommand]
    private void CloseNewTxDialog()
    {
        IsNewTxDialogOpen = false;
    }

    // Zdrojový účet (In/Out) — předvyplněný z výběru nebo ComboBox
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTxSourceAccountName))]
    [NotifyPropertyChangedFor(nameof(NewTxSourceBalance))]
    private AccountCardViewModel? _newTxSourceAccount;

    public string NewTxSourceAccountName => NewTxSourceAccount?.AccountName ?? "(vyberte účet)";
    public string NewTxSourceBalance     => NewTxSourceAccount?.BaseAmount  ?? "";

    // Režim transakce
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewTxIn))]
    [NotifyPropertyChangedFor(nameof(IsNewTxOut))]
    [NotifyPropertyChangedFor(nameof(IsNewTxTransfer))]
    private NewTxMode _newTxMode = NewTxMode.In;

    public bool IsNewTxIn       => NewTxMode == NewTxMode.In;
    public bool IsNewTxOut      => NewTxMode == NewTxMode.Out;
    public bool IsNewTxTransfer => NewTxMode == NewTxMode.Transfer;

    [RelayCommand] private void SetNewTxIn()       => NewTxMode = NewTxMode.In;
    [RelayCommand] private void SetNewTxOut()      => NewTxMode = NewTxMode.Out;
    [RelayCommand] private void SetNewTxTransfer() => NewTxMode = NewTxMode.Transfer;

    // Pole formuláře
    [ObservableProperty] private string _newTxAmount   = "";
    [ObservableProperty] private string _newTxFee      = "";
    [ObservableProperty] private string _newTxNote     = "";
    [ObservableProperty] private string _newTxCategory = "";
    [ObservableProperty] private DateTimeOffset _newTxDate = DateTimeOffset.Now;

    /// <summary>Předdefinované kategorie pro ComboBox v dialogu transakce.</summary>
    public static readonly string[] PredefinedCategories =
    [
        "Výplata", "Staking", "Odměna", "Dividenda",
        "Nájem", "Hypotéka", "Pojistné",
        "Potraviny", "Restaurace", "Doprava",
        "Zdraví", "Zábava", "Sport", "Investice", "Úspory", "Jiné",
    ];

    // Cílový účet pro Transfer
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTxTargetBalance))]
    private AccountCardViewModel? _newTxTransferTarget;

    public string NewTxTargetBalance => NewTxTransferTarget?.BaseAmount ?? "";

    [ObservableProperty] private bool _isNewTxSaving = false;

    [RelayCommand]
    private async Task SaveNewTransactionAsync()
    {
        if (NewTxSourceAccount is null) return;
        if (!decimal.TryParse(NewTxAmount.Replace(',', '.'),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
            return;

        decimal.TryParse(NewTxFee.Replace(',', '.'),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out decimal fee);

        IsNewTxSaving = true;
        try
        {
            var accountId = NewTxSourceAccount.AccountId;
            var date      = NewTxDate.UtcDateTime;
            var note      = string.IsNullOrWhiteSpace(NewTxNote)     ? null : NewTxNote.Trim();
            var category  = string.IsNullOrWhiteSpace(NewTxCategory) ? null : NewTxCategory.Trim();

            bool ok;
            if (NewTxMode == NewTxMode.Transfer)
            {
                if (NewTxTransferTarget is null) return;
                ok = await AccountRepo.AddManualTransactionAsync(
                    accountId,
                    AssetMap.Entities.Enums.TransactionType.Withdrawal,
                    (double)amount, (double)fee, date, note,
                    toAccountId: NewTxTransferTarget.AccountId,
                    category: category);
            }
            else
            {
                var txType = NewTxMode == NewTxMode.In
                    ? AssetMap.Entities.Enums.TransactionType.Deposit
                    : AssetMap.Entities.Enums.TransactionType.Withdrawal;
                ok = await AccountRepo.AddManualTransactionAsync(
                    accountId, txType, (double)amount, (double)fee, date, note,
                    category: category);
            }

            if (ok)
            {
                IsNewTxDialogOpen     = false;
                NewTxAmount           = "";
                NewTxFee              = "";
                NewTxNote             = "";
                NewTxCategory         = "";
                NewTxDate             = DateTimeOffset.Now;
                NewTxTransferTarget   = null;
                await AccountRepo.RefreshAsync();
                LoadAccounts(AccountRepo.GetAll());
            }
        }
        finally
        {
            IsNewTxSaving = false;
        }
    }

    // ── Pending CSV pro nový účet ─────────────────────────────
    private string?  _pendingCsvFileName;
    private byte[]?  _pendingCsvBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddHasCsv))]
    private string _addCsvLabel = "";

    public bool AddHasCsv => !string.IsNullOrEmpty(AddCsvLabel);

    /// <summary>Voláno z code-behind po výběru souboru v dialogu přidání účtu.</summary>
    public void AttachCsv(string fileName, byte[] bytes)
    {
        _pendingCsvFileName = fileName;
        _pendingCsvBytes    = bytes;
        AddCsvLabel         = fileName;

        // Zkus auto-vyplnit počáteční zůstatek z KB výpisu
        double? initial = AccountRepo.ParseCsvInitialBalance(bytes);
        if (initial.HasValue && double.TryParse(AddBalanceText, out _) == false)
            AddBalanceText = initial.Value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
        else if (initial.HasValue)
            AddBalanceText = initial.Value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    }

    public void ClearPendingCsv()
    {
        _pendingCsvFileName = null;
        _pendingCsvBytes    = null;
        AddCsvLabel         = "";
    }

    // ── Přehledové grafy (viditelné když nic není vybráno) ────
    public PieSliceData[] PieSlices { get; private set; } = [];

    // Raw (plná délka) — interně
    private double[]     _rawTotalHistory  = [];
    private ChartLine[]  _rawAccountLines  = [];

    // Slicované podle periody — bindovatelné
    private double[]    _totalHistory  = [];
    private ChartLine[] _accountLines  = [];

    public double[] TotalHistory
    {
        get => _totalHistory;
        private set { _totalHistory = value; OnPropertyChanged(); }
    }

    public ChartLine[] AccountLines
    {
        get => _accountLines;
        private set { _accountLines = value; OnPropertyChanged(); }
    }

    // ── Toggle: celkový součet vs. jednotlivé čáry ────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnTotalChart))]
    private bool _isOnAccountsChart = false;

    public bool IsOnTotalChart => !IsOnAccountsChart;

    [RelayCommand] private void SwitchToTotal()    => IsOnAccountsChart = false;
    [RelayCommand] private void SwitchToAllLines() => IsOnAccountsChart = true;

    // ── Perioda grafu ─────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPeriodD1))]
    [NotifyPropertyChangedFor(nameof(IsPeriodD7))]
    [NotifyPropertyChangedFor(nameof(IsPeriodM1))]
    [NotifyPropertyChangedFor(nameof(IsPeriodM3))]
    [NotifyPropertyChangedFor(nameof(IsPeriodY1))]
    private ChartPeriod _chartPeriod = ChartPeriod.M3;

    public bool IsPeriodD1 => ChartPeriod == ChartPeriod.D1;
    public bool IsPeriodD7 => ChartPeriod == ChartPeriod.D7;
    public bool IsPeriodM1 => ChartPeriod == ChartPeriod.M1;
    public bool IsPeriodM3 => ChartPeriod == ChartPeriod.M3;
    public bool IsPeriodY1 => ChartPeriod == ChartPeriod.Y1;

    [RelayCommand] private void SetPeriodD1() => ChartPeriod = ChartPeriod.D1;
    [RelayCommand] private void SetPeriodD7() => ChartPeriod = ChartPeriod.D7;
    [RelayCommand] private void SetPeriodM1() => ChartPeriod = ChartPeriod.M1;
    [RelayCommand] private void SetPeriodM3() => ChartPeriod = ChartPeriod.M3;
    [RelayCommand] private void SetPeriodY1() => ChartPeriod = ChartPeriod.Y1;

    partial void OnChartPeriodChanged(ChartPeriod value) => ApplyPeriod();

    private void ApplyPeriod()
    {
        int take = ChartPeriod switch
        {
            ChartPeriod.D1 => Math.Min(2,   _rawTotalHistory.Length),
            ChartPeriod.D7 => Math.Min(7,   _rawTotalHistory.Length),
            ChartPeriod.M1 => Math.Min(30,  _rawTotalHistory.Length),
            ChartPeriod.M3 => Math.Min(90,  _rawTotalHistory.Length),
            ChartPeriod.Y1 => _rawTotalHistory.Length,
            _              => _rawTotalHistory.Length
        };
        if (take < 2) take = 2;

        TotalHistory = _rawTotalHistory[^take..];
        AccountLines = _rawAccountLines
            .Select(l => new ChartLine
            {
                Label     = l.Label,
                Values    = l.Values.Length >= take ? l.Values[^take..] : l.Values,
                LineBrush = l.LineBrush,
            })
            .ToArray();
    }

    // ── Perioda grafu detailu účtu ────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailPeriodD7))]
    [NotifyPropertyChangedFor(nameof(IsDetailPeriodM1))]
    [NotifyPropertyChangedFor(nameof(IsDetailPeriodM3))]
    [NotifyPropertyChangedFor(nameof(IsDetailPeriodY1))]
    [NotifyPropertyChangedFor(nameof(IsDetailPeriodAll))]
    private ChartPeriod _detailChartPeriod = ChartPeriod.M3;

    public bool IsDetailPeriodD7  => DetailChartPeriod == ChartPeriod.D7;
    public bool IsDetailPeriodM1  => DetailChartPeriod == ChartPeriod.M1;
    public bool IsDetailPeriodM3  => DetailChartPeriod == ChartPeriod.M3;
    public bool IsDetailPeriodY1  => DetailChartPeriod == ChartPeriod.Y1;
    public bool IsDetailPeriodAll => DetailChartPeriod == ChartPeriod.All;

    [RelayCommand] private void SetDetailPeriodD7()  => DetailChartPeriod = ChartPeriod.D7;
    [RelayCommand] private void SetDetailPeriodM1()  => DetailChartPeriod = ChartPeriod.M1;
    [RelayCommand] private void SetDetailPeriodM3()  => DetailChartPeriod = ChartPeriod.M3;
    [RelayCommand] private void SetDetailPeriodY1()  => DetailChartPeriod = ChartPeriod.Y1;
    [RelayCommand] private void SetDetailPeriodAll() => DetailChartPeriod = ChartPeriod.All;

    partial void OnDetailChartPeriodChanged(ChartPeriod value) => ApplyDetailPeriod();
    // Filtrovaná data pro detail chart
    private double[] _detailHistory         = [];
    private int[]    _detailDepositIndices   = [];
    private int[]    _detailWithdrawalIndices = [];
    private double[] _detailPriceHistory     = [];
    private string   _detailPriceLabel       = "";

    public double[] DetailHistory
    {
        get => _detailHistory;
        private set { _detailHistory = value; OnPropertyChanged(); }
    }
    public int[] DetailDepositIndices
    {
        get => _detailDepositIndices;
        private set { _detailDepositIndices = value; OnPropertyChanged(); }
    }
    public int[] DetailWithdrawalIndices
    {
        get => _detailWithdrawalIndices;
        private set { _detailWithdrawalIndices = value; OnPropertyChanged(); }
    }
    /// <summary>Druhá čára v detailu: historická cena aktiva (USD, jen pro krypto).</summary>
    public double[] DetailPriceHistory
    {
        get => _detailPriceHistory;
        private set { _detailPriceHistory = value; OnPropertyChanged(); }
    }
    public string DetailPriceLabel
    {
        get => _detailPriceLabel;
        private set { _detailPriceLabel = value; OnPropertyChanged(); }
    }

    // Cache: symbol → 365 denních cen (USD) z CoinGecko
    private readonly Dictionary<string, double[]> _priceCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _priceFetchCts;

    private void ApplyDetailPeriod()
    {
        var acc = SelectedAccount;
        if (acc is null || acc.BalanceHistory.Length == 0)
        {
            DetailHistory            = [];
            DetailDepositIndices     = [];
            DetailWithdrawalIndices  = [];
            DetailPriceHistory       = [];
            DetailPriceLabel         = "";
            return;
        }

        int n = acc.BalanceHistory.Length;
        int take = DetailChartPeriod switch
        {
            ChartPeriod.D7  => Math.Min(7,   n),
            ChartPeriod.M1  => Math.Min(30,  n),
            ChartPeriod.M3  => Math.Min(90,  n),
            ChartPeriod.Y1  => Math.Min(365, n),
            _               => n,   // All
        };
        if (take < 2) take = Math.Min(2, n);

        int offset = n - take;
        double[] raw = acc.BalanceHistory[offset..];

        // BalanceHistory je v USD — přepočítej na nativní měnu
        // Faktor = nativní zůstatek / poslední USD bod v celé historii
        double usdLast  = acc.BalanceHistory[^1];
        double nativeScale = usdLast > 1e-9 ? acc.RawBalance / usdLast : 1.0;
        DetailHistory = raw.Select(v => v * nativeScale).ToArray();

        DetailDepositIndices     = SliceIndices(acc.DepositIndices,    offset, take);
        DetailWithdrawalIndices  = SliceIndices(acc.WithdrawalIndices, offset, take);

        // Druhá čára — cena aktiva (jen krypto a brokerage)
        if (acc.AccountTypeValue is AccountType.CryptoWallet or AccountType.Brokerage
            && _priceCache.TryGetValue(acc.BaseCurrency, out double[]? prices) && prices.Length >= 2)
        {
            int pOffset = Math.Max(0, prices.Length - n) + offset;
            int pTake   = Math.Min(take, prices.Length - pOffset);
            if (pTake >= 2)
            {
                DetailPriceHistory = prices[pOffset..(pOffset + pTake)];
                DetailPriceLabel   = acc.BaseCurrency + " cena";
            }
            else
            {
                DetailPriceHistory = [];
                DetailPriceLabel   = "";
            }
        }
        else
        {
            DetailPriceHistory = [];
            DetailPriceLabel   = "";
        }
    }

    partial void OnSelectedAccountChanged(AccountCardViewModel? value)
    {
        ApplyDetailPeriod();
        // Zahájíme fetch cen pro krypto/brokerage pokud není v cache
        if (value is { AccountTypeValue: AccountType.CryptoWallet or AccountType.Brokerage }
            && !_priceCache.ContainsKey(value.BaseCurrency))
        {
            _priceFetchCts?.Cancel();
            _priceFetchCts = new CancellationTokenSource();
            _ = FetchCryptoPriceAsync(value.BaseCurrency, _priceFetchCts.Token);
        }
    }

    private async Task FetchCryptoPriceAsync(string symbol, CancellationToken ct)
    {
        try
        {
            // Krok 1: symbol → CoinGecko coin ID
            var searchResp = await _cryptoHttp.GetAsync(
                $"search?query={Uri.EscapeDataString(symbol)}", ct);
            if (!searchResp.IsSuccessStatusCode) return;

            string? coinId = null;
            using var searchDoc = JsonDocument.Parse(await searchResp.Content.ReadAsStringAsync(ct));
            foreach (var coin in searchDoc.RootElement.GetProperty("coins").EnumerateArray())
            {
                string? sym = coin.GetProperty("symbol").GetString();
                if (string.Equals(sym, symbol, StringComparison.OrdinalIgnoreCase))
                {
                    coinId = coin.GetProperty("id").GetString();
                    break;
                }
            }
            if (coinId is null) return;

            // Krok 2: historická data (365 dní)
            await Task.Delay(300, ct); // Krátká pauza — CoinGecko rate limit
            var chartResp = await _cryptoHttp.GetAsync(
                $"coins/{Uri.EscapeDataString(coinId)}/market_chart?vs_currency=usd&days=365&interval=daily", ct);
            if (!chartResp.IsSuccessStatusCode) return;

            using var chartDoc = JsonDocument.Parse(await chartResp.Content.ReadAsStringAsync(ct));
            var priceArray = chartDoc.RootElement.GetProperty("prices");
            var prices = new List<double>();
            foreach (var entry in priceArray.EnumerateArray())
                prices.Add(entry[1].GetDouble());

            if (prices.Count < 2) return;

            _priceCache[symbol] = [.. prices];

            // Aktualizuj UI pokud je stále viditelný stejný účet
            Dispatcher.UIThread.Post(ApplyDetailPeriod);
        }
        catch (OperationCanceledException) { }
        catch { /* tichá chyba — cena prostě nebude zobrazena */ }
    }

    private static int[] SliceIndices(int[] src, int offset, int take) =>
        src.Where(i => i >= offset && i < offset + take)
           .Select(i => i - offset)
           .ToArray();

    // ── Měna pro zobrazení — synchronizovaná s AccountRepo ────
    private string _displayCurrency = AccountRepo.DisplayCurrency;
    public string DisplayCurrency
    {
        get => _displayCurrency;
        private set { _displayCurrency = value; OnPropertyChanged(); }
    }

    // ── Init ──────────────────────────────────────────────────
    public AccountsViewModel()
    {
        // Přihlásit se na event z repozitáře (price refresh každou hodinu)
        // TODO: odhlásit při dispose, pokud VM bude mít kratší životnost než App
        AccountRepo.DataRefreshed += () =>
            Dispatcher.UIThread.Post(() => LoadAccounts(AccountRepo.GetAll()));

        LoadAccounts(AccountRepo.EnsureLoaded());
    }

    // ── Refresh z API ─────────────────────────────────────────
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await AccountRepo.RefreshAsync();
        LoadAccounts(AccountRepo.GetAll());
    }

    // ── Výběr kartičky ────────────────────────────────────────
    [RelayCommand]
    private void SelectAccount(AccountCardViewModel account)
    {
        if (SelectedAccount is not null)
            SelectedAccount.IsSelected = false;

        if (SelectedAccount == account)
        {
            SelectedAccount = null;
            return;
        }

        SelectedAccount = account;
        account.IsSelected = true;
    }

    // ── Zavřít detail ─────────────────────────────────────────
    [RelayCommand]
    private void CloseDetail()
    {
        if (SelectedAccount is not null)
            SelectedAccount.IsSelected = false;
        SelectedAccount = null;
    }

    // ── Add / Edit Account dialog ─────────────────────────────
    [ObservableProperty] private bool _isAddDialogOpen = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddDialogTitle))]
    [NotifyPropertyChangedFor(nameof(AddShowAssetSection))]
    [NotifyPropertyChangedFor(nameof(AddCanConfirm))]
    [NotifyPropertyChangedFor(nameof(AddConfirmButtonText))]
    private bool _isEditMode = false;

    public string AddDialogTitle       => IsEditMode ? "Upravit účet" : "Přidat účet";
    public string AddConfirmButtonText => IsEditMode ? "Uložit"       : "Přidat účet";
    public bool   AddShowAssetSection  => !IsEditMode;

    private Guid _editingAccountId = Guid.Empty;

    // Základní pole
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddCanConfirm))]
    private string _addName = "";

    [ObservableProperty] private string _addInstitution   = "";
    [ObservableProperty] private string _addWalletAddress = "";
    /// <summary>Index BlockchainNetwork enum: 0=Bitcoin, 1=Ethereum, 2=Solana, ...</summary>
    [ObservableProperty] private int    _addWalletNetwork = 0;

    public static readonly string[] BlockchainNetworkNames =
        ["Bitcoin", "Ethereum", "Solana", "Litecoin", "BNB Smart Chain", "Polygon", "Avalanche", "Jiný"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddIsTypeBank))]
    [NotifyPropertyChangedFor(nameof(AddIsTypeCrypto))]
    [NotifyPropertyChangedFor(nameof(AddIsTypeBrokerage))]
    [NotifyPropertyChangedFor(nameof(AddIsTypeCash))]
    [NotifyPropertyChangedFor(nameof(AddAssetPlaceholder))]
    private int _addTypeIndex = 0;

    public bool AddIsTypeBank      => AddTypeIndex == 0;
    public bool AddIsTypeCrypto    => AddTypeIndex == 1;
    public bool AddIsTypeBrokerage => AddTypeIndex == 2;
    public bool AddIsTypeCash      => AddTypeIndex == 3;

    [RelayCommand] private void AddSetTypeBank()      => AddTypeIndex = 0;
    [RelayCommand] private void AddSetTypeCrypto()    => AddTypeIndex = 1;
    [RelayCommand] private void AddSetTypeBrokerage() => AddTypeIndex = 2;
    [RelayCommand] private void AddSetTypeCash()      => AddTypeIndex = 3;

    partial void OnAddTypeIndexChanged(int value)
    {
        // Při změně typu znovu ověřit aktuální symbol
        AddManualPriceText = "";
        TriggerAssetSearch(AddAssetText.Trim());
    }

    public string AddAssetPlaceholder => AddTypeIndex switch
    {
        0 or 3 => "CZK, EUR, USD, GBP, CHF…",
        1      => "BTC, ETH, USDT, SOL…",
        2      => "AAPL, MSFT, TSLA, SPY…",
        _      => "Symbol aktiva",
    };

    // ── Výběr barvy ───────────────────────────────────────────
    public static string[] AddColorOptions { get; } =
        ["#4263EB", "#00B3FF", "#00E57A", "#12B886", "#F7931A", "#FF3355", "#845EF7", "#8B8B9A"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddColor0))]
    [NotifyPropertyChangedFor(nameof(IsAddColor1))]
    [NotifyPropertyChangedFor(nameof(IsAddColor2))]
    [NotifyPropertyChangedFor(nameof(IsAddColor3))]
    [NotifyPropertyChangedFor(nameof(IsAddColor4))]
    [NotifyPropertyChangedFor(nameof(IsAddColor5))]
    [NotifyPropertyChangedFor(nameof(IsAddColor6))]
    [NotifyPropertyChangedFor(nameof(IsAddColor7))]
    private int _addColorIndex = 0;

    public bool IsAddColor0 => AddColorIndex == 0;
    public bool IsAddColor1 => AddColorIndex == 1;
    public bool IsAddColor2 => AddColorIndex == 2;
    public bool IsAddColor3 => AddColorIndex == 3;
    public bool IsAddColor4 => AddColorIndex == 4;
    public bool IsAddColor5 => AddColorIndex == 5;
    public bool IsAddColor6 => AddColorIndex == 6;
    public bool IsAddColor7 => AddColorIndex == 7;

    [RelayCommand] private void AddSelectColor0() => AddColorIndex = 0;
    [RelayCommand] private void AddSelectColor1() => AddColorIndex = 1;
    [RelayCommand] private void AddSelectColor2() => AddColorIndex = 2;
    [RelayCommand] private void AddSelectColor3() => AddColorIndex = 3;
    [RelayCommand] private void AddSelectColor4() => AddColorIndex = 4;
    [RelayCommand] private void AddSelectColor5() => AddColorIndex = 5;
    [RelayCommand] private void AddSelectColor6() => AddColorIndex = 6;
    [RelayCommand] private void AddSelectColor7() => AddColorIndex = 7;

    public string AddSelectedColorHex =>
        AddColorIndex < AddColorOptions.Length ? AddColorOptions[AddColorIndex] : AddColorOptions[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddCanConfirm))]
    private string _addBalanceText = "";

    // ── Validace aktiva (CoinGecko / known fiat) ──────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddIsAssetNone))]
    [NotifyPropertyChangedFor(nameof(AddIsAssetSearching))]
    [NotifyPropertyChangedFor(nameof(AddIsAssetFound))]
    [NotifyPropertyChangedFor(nameof(AddIsAssetNotFound))]
    [NotifyPropertyChangedFor(nameof(AddIsAssetError))]
    [NotifyPropertyChangedFor(nameof(AddIsAssetManual))]
    [NotifyPropertyChangedFor(nameof(AddShowManualPrice))]
    [NotifyPropertyChangedFor(nameof(AddCanConfirm))]
    private AssetSearchStatus _addAssetStatus = AssetSearchStatus.None;

    public bool AddIsAssetNone      => AddAssetStatus == AssetSearchStatus.None;
    public bool AddIsAssetSearching => AddAssetStatus == AssetSearchStatus.Searching;
    public bool AddIsAssetFound     => AddAssetStatus == AssetSearchStatus.Found;
    public bool AddIsAssetNotFound  => AddAssetStatus == AssetSearchStatus.NotFound;
    public bool AddIsAssetError     => AddAssetStatus == AssetSearchStatus.Error;
    public bool AddIsAssetManual    => AddAssetStatus == AssetSearchStatus.Manual;
    public bool AddShowManualPrice  => AddAssetStatus == AssetSearchStatus.NotFound
                                    || AddAssetStatus == AssetSearchStatus.Manual;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddCanConfirm))]
    private string _addAssetText = "";

    [ObservableProperty] private string _addAssetFoundName = "";
    private double _addAssetUsdPrice = 1.0;
    private string _addAssetSymbol   = "";

    partial void OnAddAssetTextChanged(string value)
    {
        // Nové hledání → zahodit ruční cenu
        AddManualPriceText = "";
        TriggerAssetSearch(value.Trim());
    }

    // Ruční cena — záloha pro akcie/ETF (CoinGecko krypto only)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddCanConfirm))]
    private string _addManualPriceText = "";

    partial void OnAddManualPriceTextChanged(string value)
    {
        if (AddAssetStatus != AssetSearchStatus.NotFound &&
            AddAssetStatus != AssetSearchStatus.Manual) return;

        if (double.TryParse(value.Replace(',', '.'), NumberStyles.Any,
                            CultureInfo.InvariantCulture, out double price) && price > 0)
        {
            _addAssetUsdPrice = price;
            _addAssetSymbol   = AddAssetText.Trim().ToUpper();
            AddAssetFoundName = $"Ruční cena: {price:N2} USD";
            AddAssetStatus    = AssetSearchStatus.Manual;
        }
        else if (AddAssetStatus == AssetSearchStatus.Manual)
        {
            AddAssetFoundName = "";
            AddAssetStatus    = AssetSearchStatus.NotFound;
        }
    }

    // ── HTTP klienti ──────────────────────────────────────────
    private static readonly HttpClient _cryptoHttp = MakeHttp("https://api.coingecko.com/api/v3/");
    private static readonly HttpClient _stockHttp  = MakeHttp("https://query1.finance.yahoo.com/");

    private static HttpClient MakeHttp(string baseUrl)
    {
        var c = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(12) };
        c.DefaultRequestHeaders.Add("User-Agent", "AssetMap/0.1 (portfolio tracker)");
        c.DefaultRequestHeaders.Add("Accept",     "application/json");
        return c;
    }

    // Fiat: symboly → zobrazovaný název (rate se čte dynamicky z FxRates)
    private static readonly Dictionary<string, string> _fiatNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Kč"]  = "Česká koruna",
            ["CZK"] = "Česká koruna",
            ["EUR"] = "Euro",
            ["USD"] = "Americký dolar",
            ["GBP"] = "Britská libra",
            ["CHF"] = "Švýcarský frank",
            ["JPY"] = "Japonský jen",
            ["PLN"] = "Polský zlotý",
        };

    private static bool TryLookupFiat(string symbol, out string name, out double usdRate)
    {
        if (!_fiatNames.TryGetValue(symbol, out name!)) { usdRate = 0; return false; }
        string code = symbol.Equals("kč", StringComparison.OrdinalIgnoreCase) ? "CZK" : symbol.ToUpperInvariant();
        usdRate = FxRates.FiatToUsd(code);
        return true;
    }

    private CancellationTokenSource? _assetSearchCts;

    private void TriggerAssetSearch(string symbol)
    {
        _assetSearchCts?.Cancel();
        _assetSearchCts?.Dispose();
        _assetSearchCts = new CancellationTokenSource();

        if (string.IsNullOrWhiteSpace(symbol))
        {
            AddAssetStatus = AssetSearchStatus.None;
            return;
        }

        var ct = _assetSearchCts.Token;
        _ = AddTypeIndex switch
        {
            0 or 3 => SearchFiatOnlyAsync(symbol, ct),    // Banka / Hotovost
            1      => SearchCryptoAsync(symbol, ct),       // Krypto → CoinGecko
            2      => SearchStockAsync(symbol, ct),        // Broker → Yahoo Finance
            _      => SearchCryptoAsync(symbol, ct),
        };
    }

    // ── Banka / Hotovost: jen fiat slovník ───────────────────
    private Task SearchFiatOnlyAsync(string symbol, CancellationToken ct)
    {
        if (TryLookupFiat(symbol, out string name, out double usdRate))
        {
            _addAssetSymbol   = symbol.Equals("kč", StringComparison.OrdinalIgnoreCase) ? "Kč" : symbol.ToUpperInvariant();
            _addAssetUsdPrice = usdRate;
            AddAssetFoundName = name;
            AddAssetStatus    = AssetSearchStatus.Found;
        }
        else
        {
            AddAssetFoundName = "Neplatná měna. Zadej např.: CZK, EUR, USD, GBP, CHF";
            AddAssetStatus    = AssetSearchStatus.NotFound;
        }
        return Task.CompletedTask;
    }

    // ── Krypto: fiat fallback → CoinGecko ────────────────────
    private async Task SearchCryptoAsync(string symbol, CancellationToken ct)
    {
        // Fiat fallback (např. EUR na crypto exchange)
        if (TryLookupFiat(symbol, out string fname, out double fusdRate))
        {
            Dispatcher.UIThread.Post(() =>
            {
                _addAssetSymbol   = symbol.Equals("kč", StringComparison.OrdinalIgnoreCase) ? "Kč" : symbol.ToUpperInvariant();
                _addAssetUsdPrice = fusdRate;
                AddAssetFoundName = fname;
                AddAssetStatus    = AssetSearchStatus.Found;
            });
            return;
        }

        try { await Task.Delay(450, ct); } catch (OperationCanceledException) { return; }
        Dispatcher.UIThread.Post(() => AddAssetStatus = AssetSearchStatus.Searching);

        try
        {
            var searchResp = await _cryptoHttp.GetAsync(
                $"search?query={Uri.EscapeDataString(symbol)}", ct);

            if (!searchResp.IsSuccessStatusCode)
            {
                if (ct.IsCancellationRequested) return;
                string msg = (int)searchResp.StatusCode switch
                {
                    429       => "CoinGecko: rate limit — zkus znovu za chvíli",
                    401 or 403 => "CoinGecko: přístup odepřen",
                    _         => $"CoinGecko chyba {(int)searchResp.StatusCode}",
                };
                Dispatcher.UIThread.Post(() => { AddAssetFoundName = msg; AddAssetStatus = AssetSearchStatus.Error; });
                return;
            }

            var searchJson = await searchResp.Content.ReadAsStringAsync(ct);
            string? coinId = null, coinName = null;
            using var doc = JsonDocument.Parse(searchJson);
            foreach (var coin in doc.RootElement.GetProperty("coins").EnumerateArray())
            {
                string? sym = coin.GetProperty("symbol").GetString();
                if (string.Equals(sym, symbol, StringComparison.OrdinalIgnoreCase))
                {
                    coinId   = coin.GetProperty("id").GetString();
                    coinName = coin.GetProperty("name").GetString();
                    break;
                }
            }

            if (coinId == null)
            {
                if (!ct.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() => AddAssetStatus = AssetSearchStatus.NotFound);
                return;
            }

            double usdPrice = 0;
            try
            {
                var priceResp = await _cryptoHttp.GetAsync(
                    $"simple/price?ids={Uri.EscapeDataString(coinId)}&vs_currencies=usd", ct);
                if (priceResp.IsSuccessStatusCode)
                {
                    var priceJson = await priceResp.Content.ReadAsStringAsync(ct);
                    using var pd = JsonDocument.Parse(priceJson);
                    if (pd.RootElement.TryGetProperty(coinId, out var entry) &&
                        entry.TryGetProperty("usd", out var usdEl))
                        usdPrice = usdEl.GetDouble();
                }
            }
            catch { /* cena selhala */ }

            if (ct.IsCancellationRequested) return;
            string fs = symbol.ToUpperInvariant(), fn = coinName ?? fs;
            Dispatcher.UIThread.Post(() =>
            {
                _addAssetSymbol   = fs;
                _addAssetUsdPrice = usdPrice;
                AddAssetFoundName = usdPrice > 0 ? $"{fn}  ≈  ${usdPrice:N2}" : fn;
                AddAssetStatus    = AssetSearchStatus.Found;
            });
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException)
        {
            if (!ct.IsCancellationRequested)
                Dispatcher.UIThread.Post(() =>
                {
                    AddAssetFoundName = "Nepodařilo se připojit k CoinGecko";
                    AddAssetStatus    = AssetSearchStatus.Error;
                });
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                Dispatcher.UIThread.Post(() =>
                {
                    AddAssetFoundName = ex.Message.Length > 60 ? "Neočekávaná chyba" : ex.Message;
                    AddAssetStatus    = AssetSearchStatus.Error;
                });
        }
    }

    // ── Broker: fiat fallback → Yahoo Finance ────────────────
    private async Task SearchStockAsync(string symbol, CancellationToken ct)
    {
        // Fiat fallback (cash na brokerage účtu)
        if (TryLookupFiat(symbol, out string sfname, out double sfusdRate))
        {
            Dispatcher.UIThread.Post(() =>
            {
                _addAssetSymbol   = symbol.ToUpperInvariant();
                _addAssetUsdPrice = sfusdRate;
                AddAssetFoundName = sfname;
                AddAssetStatus    = AssetSearchStatus.Found;
            });
            return;
        }

        try { await Task.Delay(450, ct); } catch (OperationCanceledException) { return; }
        Dispatcher.UIThread.Post(() => AddAssetStatus = AssetSearchStatus.Searching);

        try
        {
            var resp = await _stockHttp.GetAsync(
                $"v8/finance/chart/{Uri.EscapeDataString(symbol.ToUpperInvariant())}?interval=1d&range=1d", ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound ||
                resp.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                if (!ct.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() => AddAssetStatus = AssetSearchStatus.NotFound);
                return;
            }

            if (!resp.IsSuccessStatusCode)
            {
                if (ct.IsCancellationRequested) return;
                Dispatcher.UIThread.Post(() =>
                {
                    AddAssetFoundName = $"Yahoo Finance chyba {(int)resp.StatusCode}";
                    AddAssetStatus    = AssetSearchStatus.Error;
                });
                return;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            if (ct.IsCancellationRequested) return;

            using var doc  = JsonDocument.Parse(json);
            var result     = doc.RootElement.GetProperty("chart").GetProperty("result");

            if (result.ValueKind == JsonValueKind.Null || result.GetArrayLength() == 0)
            {
                Dispatcher.UIThread.Post(() => AddAssetStatus = AssetSearchStatus.NotFound);
                return;
            }

            var meta    = result[0].GetProperty("meta");
            double price = meta.TryGetProperty("regularMarketPrice", out var priceEl)
                           ? priceEl.GetDouble() : 0;
            string cur  = meta.TryGetProperty("currency", out var curEl)
                           ? (curEl.GetString() ?? "USD") : "USD";
            string name = meta.TryGetProperty("longName", out var nameEl) && nameEl.GetString() is { } ln
                           ? ln
                           : (meta.TryGetProperty("shortName", out var snEl) ? snEl.GetString() ?? symbol.ToUpperInvariant() : symbol.ToUpperInvariant());

            // Převod na USD pokud cena není v USD (EUR, GBP... akcie na EU burzách)
            double usdPrice = price;
            if (!string.Equals(cur, "USD", StringComparison.OrdinalIgnoreCase))
                usdPrice = price * FxRates.FiatToUsd(cur);

            string fs = symbol.ToUpperInvariant();
            Dispatcher.UIThread.Post(() =>
            {
                _addAssetSymbol   = fs;
                _addAssetUsdPrice = usdPrice;
                AddAssetFoundName = usdPrice > 0
                    ? $"{name}  ≈  ${usdPrice:N2} / akcie"
                    : name;
                AddAssetStatus    = AssetSearchStatus.Found;
            });
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException)
        {
            if (!ct.IsCancellationRequested)
                Dispatcher.UIThread.Post(() =>
                {
                    AddAssetFoundName = "Nepodařilo se připojit k Yahoo Finance";
                    AddAssetStatus    = AssetSearchStatus.Error;
                });
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                Dispatcher.UIThread.Post(() =>
                {
                    AddAssetFoundName = ex.Message.Length > 60 ? "Neočekávaná chyba" : ex.Message;
                    AddAssetStatus    = AssetSearchStatus.Error;
                });
        }
    }

    // ── Validace formuláře ────────────────────────────────────
    private static bool ValidBalance(string text) =>
        double.TryParse(text.Replace(',', '.'),
            NumberStyles.Any, CultureInfo.InvariantCulture, out double v) && v >= 0;

    public bool AddCanConfirm =>
        !string.IsNullOrWhiteSpace(AddName) &&
        (IsEditMode || ((AddAssetStatus == AssetSearchStatus.Found || AddAssetStatus == AssetSearchStatus.Manual)
                        && ValidBalance(AddBalanceText)));

    private AccountType ResolvedAddType => AddTypeIndex switch
    {
        1 => AccountType.CryptoWallet,
        2 => AccountType.Brokerage,
        3 => AccountType.Cash,
        _ => AccountType.Bank,
    };

    // ── Příkazy dialogu ───────────────────────────────────────
    [RelayCommand]
    private void AddAccount()
    {
        IsEditMode     = false;
        _editingAccountId = Guid.Empty;
        AddColorIndex  = 0;
        IsAddDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditAccount()
    {
        if (SelectedAccount == null) return;
        var sa = SelectedAccount;

        IsEditMode        = true;
        _editingAccountId = sa.AccountId;
        AddName           = sa.AccountName;
        AddInstitution    = sa.Institution;
        AddTypeIndex      = sa.AccountTypeValue switch
        {
            AccountType.CryptoWallet => 1,
            AccountType.Brokerage    => 2,
            AccountType.Cash         => 3,
            _                        => 0,
        };
        // Barva — najdi index nebo default 0
        int ci = Array.FindIndex(AddColorOptions,
            c => string.Equals(c, sa.IconColorHex, StringComparison.OrdinalIgnoreCase));
        AddColorIndex = ci >= 0 ? ci : 0;

        // Asset — jen pro zobrazení v edit mode (read-only)
        _addAssetSymbol   = sa.BaseCurrency;
        AddAssetText      = sa.BaseCurrency;
        AddAssetFoundName = sa.BaseCurrency;
        AddAssetStatus    = AssetSearchStatus.Found;
        _addAssetUsdPrice = AccountRepo.UsdRateForSymbol(sa.BaseCurrency);

        IsAddDialogOpen = true;
    }

    [RelayCommand]
    private void DeleteAccount()
    {
        if (SelectedAccount == null) return;
        AccountRepo.RemoveAccount(SelectedAccount.AccountId);
    }

    [RelayCommand]
    private void CancelAddAccount()
    {
        IsAddDialogOpen = false;
        ResetAddForm();
    }

    [RelayCommand]
    private void ConfirmAddAccount()
    {
        if (!AddCanConfirm) return;

        if (IsEditMode)
        {
            AccountRepo.UpdateAccount(_editingAccountId, AddName, AddInstitution,
                ResolvedAddType, AddSelectedColorHex);
            IsAddDialogOpen = false;
            ResetAddForm();
        }
        else
        {
            double.TryParse(AddBalanceText.Replace(',', '.'),
                NumberStyles.Any, CultureInfo.InvariantCulture, out double balance);

            // Pokud je připojen CSV výpis, použij 0 jako základ — import ho nastaví správně
            double startBalance = _pendingCsvBytes is not null ? 0 : balance;

            string? walletAddr = string.IsNullOrWhiteSpace(AddWalletAddress) ? null : AddWalletAddress.Trim();
            AccountRepo.AddAccount(AddName, AddInstitution, ResolvedAddType,
                _addAssetSymbol, startBalance, _addAssetUsdPrice, AddSelectedColorHex,
                walletAddr, walletAddr != null ? AddWalletNetwork : null);

            IsAddDialogOpen = false;

            // Spusť import v pozadí (pokud byl CSV připojen)
            var csvBytes = _pendingCsvBytes;
            var csvName  = _pendingCsvFileName ?? "import.csv";
            ResetAddForm();

            if (csvBytes is not null)
            {
                _ = Task.Run(async () =>
                {
                    // Počkej na potvrzení vytvoření účtu (server potřebuje chvíli)
                    await Task.Delay(800);
                    await AccountRepo.RefreshAsync();
                    var created = AccountRepo.GetAll().LastOrDefault();
                    if (created is null) return;
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                        await RunImportAsync(created.Account.Id, csvName, csvBytes));
                });
            }
        }
    }

    private void ResetAddForm()
    {
        IsEditMode        = false;
        _editingAccountId = Guid.Empty;
        AddName           = "";
        AddInstitution    = "";
        AddTypeIndex      = 0;
        AddColorIndex     = 0;
        AddAssetText       = "";
        AddAssetFoundName  = "";
        AddAssetStatus     = AssetSearchStatus.None;
        AddManualPriceText = "";
        AddBalanceText     = "";
        AddWalletAddress   = "";
        AddWalletNetwork   = 0;
        _addAssetSymbol   = "";
        _addAssetUsdPrice = 1.0;
        _assetSearchCts?.Cancel();
        ClearPendingCsv();
    }

    // ── Načtení dat z repo ────────────────────────────────────
    private void LoadAccounts(IReadOnlyList<AccountData> data)
    {
        DisplayCurrency = AccountRepo.DisplayCurrency;
        Accounts.Clear();
        SelectedAccount = null;

        foreach (var d in data)
        {
            var vm = AccountCardViewModel.FromData(d);
            vm.OnSelect = a => SelectAccount(a);
            Accounts.Add(vm);
        }

        ComputeOverview();
    }

    // ── Výpočet přehledových dat ──────────────────────────────
    private void ComputeOverview()
    {
        var accs = Accounts.ToArray();
        if (accs.Length == 0) return;

        double[] normalized = accs.Select(a => a.RawBalance * a.ConversionRate).ToArray();
        double   totalNorm  = normalized.Sum();

        // Koláčový graf
        PieSlices = accs.Select((a, i) => new PieSliceData
        {
            Label      = a.AccountName,
            Value      = normalized[i],
            Percent    = (totalNorm > 0 ? normalized[i] / totalNorm * 100 : 0).ToString("N1") + " %",
            SliceBrush = a.IconBrush,
        }).ToArray();
        OnPropertyChanged(nameof(PieSlices));

        // Celková linie (raw — plná délka)
        // BalanceHistory je vždy v USD → sečteme USD hodnoty a převedeme na zobrazovací měnu
        int    len          = accs[0].BalanceHistory.Length;
        double usdToDisplay = AccountRepo.UsdToDisplay;
        _rawTotalHistory = Enumerable.Range(0, len)
            .Select(i => accs.Sum(a => a.BalanceHistory[i]) * usdToDisplay)
            .ToArray();

        // Čáry jednotlivých účtů v % změně od začátku (raw)
        _rawAccountLines = accs.Select(a =>
        {
            double start = a.BalanceHistory[0];
            if (start == 0) start = 1;
            return new ChartLine
            {
                Label     = a.AccountName,
                Values    = a.BalanceHistory.Select(v => (v - start) / start * 100).ToArray(),
                LineBrush = a.IconBrush,
            };
        }).ToArray();

        ApplyPeriod();
    }
}
