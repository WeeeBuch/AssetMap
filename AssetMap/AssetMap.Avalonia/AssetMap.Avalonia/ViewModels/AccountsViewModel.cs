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
using AssetMap.Repos.Accounts;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetMap.Avalonia.ViewModels;

public enum AssetSearchStatus { None, Searching, Found, NotFound, Error }

public partial class AccountsViewModel : ViewModelBase
{
    // ── Kolekce účtů ─────────────────────────────────────────
    public ObservableCollection<AccountCardViewModel> Accounts { get; } = [];

    // ── Vybraný účet ─────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAccount))]
    private AccountCardViewModel? _selectedAccount;

    public bool HasSelectedAccount => SelectedAccount is not null;

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

    [ObservableProperty] private string _addInstitution = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddIsTypeBank))]
    [NotifyPropertyChangedFor(nameof(AddIsTypeCrypto))]
    [NotifyPropertyChangedFor(nameof(AddIsTypeBrokerage))]
    [NotifyPropertyChangedFor(nameof(AddIsTypeCash))]
    private int _addTypeIndex = 0;

    public bool AddIsTypeBank      => AddTypeIndex == 0;
    public bool AddIsTypeCrypto    => AddTypeIndex == 1;
    public bool AddIsTypeBrokerage => AddTypeIndex == 2;
    public bool AddIsTypeCash      => AddTypeIndex == 3;

    [RelayCommand] private void AddSetTypeBank()      => AddTypeIndex = 0;
    [RelayCommand] private void AddSetTypeCrypto()    => AddTypeIndex = 1;
    [RelayCommand] private void AddSetTypeBrokerage() => AddTypeIndex = 2;
    [RelayCommand] private void AddSetTypeCash()      => AddTypeIndex = 3;

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
    [NotifyPropertyChangedFor(nameof(AddCanConfirm))]
    private AssetSearchStatus _addAssetStatus = AssetSearchStatus.None;

    public bool AddIsAssetNone      => AddAssetStatus == AssetSearchStatus.None;
    public bool AddIsAssetSearching => AddAssetStatus == AssetSearchStatus.Searching;
    public bool AddIsAssetFound     => AddAssetStatus == AssetSearchStatus.Found;
    public bool AddIsAssetNotFound  => AddAssetStatus == AssetSearchStatus.NotFound;
    public bool AddIsAssetError     => AddAssetStatus == AssetSearchStatus.Error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddCanConfirm))]
    private string _addAssetText = "";

    [ObservableProperty] private string _addAssetFoundName = "";
    private double _addAssetUsdPrice = 1.0;
    private string _addAssetSymbol   = "";

    partial void OnAddAssetTextChanged(string value) => TriggerAssetSearch(value.Trim());

    // ── HTTP klient (sdílený, bez auth — CoinGecko free) ─────
    private static readonly HttpClient _assetHttp = CreateAssetHttpClient();

    private static HttpClient CreateAssetHttpClient()
    {
        var c = new HttpClient
        {
            BaseAddress = new Uri("https://api.coingecko.com/api/v3/"),
            Timeout     = TimeSpan.FromSeconds(12),
        };
        c.DefaultRequestHeaders.Add("User-Agent", "AssetMap/0.1 (portfolio tracker)");
        c.DefaultRequestHeaders.Add("Accept",     "application/json");
        return c;
    }

    // Fiat: symbol → (zobrazovaný název, kurz→USD)
    private static readonly Dictionary<string, (string Name, double UsdRate)> _knownFiat =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Kč"]  = ("Česká koruna",    0.0432),
            ["CZK"] = ("Česká koruna",    0.0432),
            ["EUR"] = ("Euro",            1.08),
            ["USD"] = ("Americký dolar",  1.0),
            ["GBP"] = ("Britská libra",   1.256),
            ["CHF"] = ("Švýcarský frank", 1.137),
            ["JPY"] = ("Japonský jen",    0.0065),
            ["PLN"] = ("Polský zlotý",    0.245),
        };

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
        _ = SearchAssetAsync(symbol, _assetSearchCts.Token);
    }

    private async Task SearchAssetAsync(string symbol, CancellationToken ct)
    {
        // ── 1. Zkontroluj known fiat (bez API) ────────────────
        if (_knownFiat.TryGetValue(symbol, out var fiat))
        {
            Dispatcher.UIThread.Post(() =>
            {
                _addAssetSymbol   = symbol.Equals("kč", StringComparison.OrdinalIgnoreCase) ? "Kč" : symbol.ToUpperInvariant();
                _addAssetUsdPrice = fiat.UsdRate;
                AddAssetFoundName = fiat.Name;
                AddAssetStatus    = AssetSearchStatus.Found;
            });
            return;
        }

        // ── 2. Debounce 450 ms před voláním API ───────────────
        try { await Task.Delay(450, ct); }
        catch (OperationCanceledException) { return; }

        Dispatcher.UIThread.Post(() => AddAssetStatus = AssetSearchStatus.Searching);

        try
        {
            // ── 3. CoinGecko search ───────────────────────────
            var searchResp = await _assetHttp.GetAsync(
                $"search?query={Uri.EscapeDataString(symbol)}", ct);

            if (!searchResp.IsSuccessStatusCode)
            {
                if (ct.IsCancellationRequested) return;
                string errMsg = (int)searchResp.StatusCode switch
                {
                    429 => "CoinGecko: rate limit — zkus znovu za chvíli",
                    401 or 403 => "CoinGecko: přístup odepřen (401/403)",
                    _ => $"CoinGecko chyba {(int)searchResp.StatusCode}",
                };
                Dispatcher.UIThread.Post(() =>
                {
                    AddAssetFoundName = errMsg;
                    AddAssetStatus    = AssetSearchStatus.Error;
                });
                return;
            }

            var searchJson = await searchResp.Content.ReadAsStringAsync(ct);

            string? coinId   = null;
            string? coinName = null;

            using var doc = JsonDocument.Parse(searchJson);
            foreach (var coin in doc.RootElement.GetProperty("coins").EnumerateArray())
            {
                string? sym = coin.GetProperty("symbol").GetString();
                if (string.Equals(sym, symbol, StringComparison.OrdinalIgnoreCase))
                {
                    coinId   = coin.GetProperty("id").GetString();
                    coinName = coin.GetProperty("name").GetString();
                    break; // první výsledek = nejvyšší market cap rank
                }
            }

            if (coinId == null)
            {
                if (!ct.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() => AddAssetStatus = AssetSearchStatus.NotFound);
                return;
            }

            // ── 4. Načti aktuální USD cenu ────────────────────
            double usdPrice = 0;
            try
            {
                var priceResp = await _assetHttp.GetAsync(
                    $"simple/price?ids={Uri.EscapeDataString(coinId)}&vs_currencies=usd", ct);
                if (priceResp.IsSuccessStatusCode)
                {
                    var priceJson = await priceResp.Content.ReadAsStringAsync(ct);
                    using var priceDoc = JsonDocument.Parse(priceJson);
                    if (priceDoc.RootElement.TryGetProperty(coinId, out var entry) &&
                        entry.TryGetProperty("usd", out var usdEl))
                        usdPrice = usdEl.GetDouble();
                }
            }
            catch { /* cena selhala — přidáme s cenou 0, opravíme při price refresh */ }

            if (ct.IsCancellationRequested) return;

            string finalSymbol = symbol.ToUpperInvariant();
            string finalName   = coinName ?? finalSymbol;
            Dispatcher.UIThread.Post(() =>
            {
                _addAssetSymbol   = finalSymbol;
                _addAssetUsdPrice = usdPrice;
                AddAssetFoundName = usdPrice > 0
                    ? $"{finalName}  ≈  ${usdPrice:N2} / {finalSymbol}"
                    : finalName;
                AddAssetStatus = AssetSearchStatus.Found;
            });
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException ex)
        {
            if (!ct.IsCancellationRequested)
                Dispatcher.UIThread.Post(() =>
                {
                    AddAssetFoundName = ex.Message.Length > 60
                        ? "Nepodařilo se připojit k CoinGecko"
                        : ex.Message;
                    AddAssetStatus = AssetSearchStatus.Error;
                });
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                Dispatcher.UIThread.Post(() =>
                {
                    AddAssetFoundName = ex.Message.Length > 60
                        ? "Neočekávaná chyba při hledání"
                        : ex.Message;
                    AddAssetStatus = AssetSearchStatus.Error;
                });
        }
    }

    // ── Validace formuláře ────────────────────────────────────
    private static bool ValidBalance(string text) =>
        double.TryParse(text.Replace(',', '.'),
            NumberStyles.Any, CultureInfo.InvariantCulture, out double v) && v >= 0;

    public bool AddCanConfirm =>
        !string.IsNullOrWhiteSpace(AddName) &&
        (IsEditMode || (AddAssetStatus == AssetSearchStatus.Found && ValidBalance(AddBalanceText)));

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
        }
        else
        {
            double.TryParse(AddBalanceText.Replace(',', '.'),
                NumberStyles.Any, CultureInfo.InvariantCulture, out double balance);
            AccountRepo.AddAccount(AddName, AddInstitution, ResolvedAddType,
                _addAssetSymbol, balance, _addAssetUsdPrice, AddSelectedColorHex);
        }

        IsAddDialogOpen = false;
        ResetAddForm();
    }

    private void ResetAddForm()
    {
        IsEditMode        = false;
        _editingAccountId = Guid.Empty;
        AddName           = "";
        AddInstitution    = "";
        AddTypeIndex      = 0;
        AddColorIndex     = 0;
        AddAssetText      = "";
        AddAssetFoundName = "";
        AddAssetStatus    = AssetSearchStatus.None;
        AddBalanceText    = "";
        _addAssetSymbol   = "";
        _addAssetUsdPrice = 1.0;
        _assetSearchCts?.Cancel();
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
        int len = accs[0].BalanceHistory.Length;
        _rawTotalHistory = Enumerable.Range(0, len)
            .Select(i => accs.Sum(a => a.BalanceHistory[i] * a.ConversionRate))
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
