using System.Collections.Generic;
using System.Linq;
using AssetMap.Avalonia.Services;
using AssetMap.Entities.Enums;
using AssetMap.Repos;
using AssetMap.Repos.Accounts;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetMap.Avalonia.ViewModels;

public enum AppPage { Dashboard, Accounts, Transactions, Assets, Settings }

public partial class MainViewModel : ViewModelBase
{
    // ── Navigace ──────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(IsOnDashboard))]
    [NotifyPropertyChangedFor(nameof(IsOnAccounts))]
    [NotifyPropertyChangedFor(nameof(IsOnTransactions))]
    [NotifyPropertyChangedFor(nameof(IsOnAssets))]
    [NotifyPropertyChangedFor(nameof(IsOnSettings))]
    private AppPage _currentPage = AppPage.Dashboard;

    public bool IsOnDashboard    => CurrentPage == AppPage.Dashboard;
    public bool IsOnAccounts     => CurrentPage == AppPage.Accounts;
    public bool IsOnTransactions => CurrentPage == AppPage.Transactions;
    public bool IsOnAssets       => CurrentPage == AppPage.Assets;
    public bool IsOnSettings     => CurrentPage == AppPage.Settings;

    public string PageTitle => CurrentPage switch
    {
        AppPage.Dashboard    => "Dashboard",
        AppPage.Accounts     => "Účty",
        AppPage.Transactions => "Transakce",
        AppPage.Assets       => "Aktiva",
        AppPage.Settings     => "Nastavení",
        _                    => "AssetMap"
    };

    // Oddělený VM pro Accounts (má vlastní kolekci + selected state)
    public AccountsViewModel AccountsVM { get; }

    // VM pro stránku Transakcí
    public TransactionsViewModel TransactionsVM { get; }

    // ── Dashboard data ────────────────────────────────────────
    private string _dashboardTotal    = "–";
    private string _dashboardChange   = "";
    private bool   _dashboardPositive;
    private IReadOnlyList<TransactionDisplayItem> _dashboardRecentTxs = [];

    public string DashboardTotal
    {
        get => _dashboardTotal;
        private set { _dashboardTotal = value; OnPropertyChanged(); }
    }
    public string DashboardChange
    {
        get => _dashboardChange;
        private set { _dashboardChange = value; OnPropertyChanged(); }
    }
    public bool DashboardPositive
    {
        get => _dashboardPositive;
        private set { _dashboardPositive = value; OnPropertyChanged(); }
    }
    public IReadOnlyList<TransactionDisplayItem> DashboardRecentTxs
    {
        get => _dashboardRecentTxs;
        private set { _dashboardRecentTxs = value; OnPropertyChanged(); }
    }

    public MainViewModel()
    {
        AccountsVM = new AccountsViewModel();
        TransactionsVM = new TransactionsViewModel();
        BuildDashboard();
        // Dashboard se taky aktualizuje po každém price refreshi
        AccountRepo.DataRefreshed += BuildDashboard;
        PriceRefreshService.Start();
    }

    private void BuildDashboard()
    {
        var history = AccountsVM.TotalHistory;
        if (history.Length == 0) { DashboardTotal = "0"; return; }

        double now     = history[^1];
        double prev    = history.Length > 1 ? history[^2] : now;
        double chgPct  = prev > 0 ? (now - prev) / prev * 100 : 0;
        double chgAbs  = now - prev;

        DashboardTotal    = FormatCurrency(now);
        DashboardChange   = (chgPct >= 0 ? "▲ +" : "▼ ")
                            + FormatCurrency(System.Math.Abs(chgAbs))
                            + "  " + (chgPct >= 0 ? "+" : "") + chgPct.ToString("N2") + " %";
        DashboardPositive = chgPct >= 0;

        var data        = AccountRepo.GetAll();
        var creditBrush = new SolidColorBrush(Color.Parse("#00E57A"));
        var debitBrush  = new SolidColorBrush(Color.Parse("#FF3355"));

        DashboardRecentTxs = data
            .SelectMany(d => d.RecentTransactions.Select(tx => (tx, d.BaseCurrency)))
            .OrderByDescending(x => x.tx.Date)
            .Take(8)
            .Select(x =>
            {
                bool isCredit = x.tx.Type == TransactionType.Deposit;
                return new TransactionDisplayItem(
                    isCredit ? "↓" : "↑",
                    isCredit ? (IBrush)creditBrush : debitBrush,
                    isCredit ? "Příchozí platba" : "Odchozí platba",
                    x.tx.Date.ToString("dd. MM. yyyy"),
                    (isCredit ? "+" : "−") + ((double)x.tx.Quantity).ToString("N2") + " " + x.BaseCurrency,
                    isCredit);
            })
            .ToList();
    }

    private static string FormatCurrency(double v) =>
        ((long)System.Math.Round(v))
            .ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    // ── Sidebar toggle ────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool _isSidebarOpen = true;

    public double SidebarWidth => IsSidebarOpen ? 220.0 : 56.0;

    [RelayCommand] private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand] private void NavigateToDashboard()    => CurrentPage = AppPage.Dashboard;
    [RelayCommand] private void NavigateToAccounts()     => CurrentPage = AppPage.Accounts;
    [RelayCommand] private void NavigateToTransactions() => CurrentPage = AppPage.Transactions;
    [RelayCommand] private void NavigateToAssets()       => CurrentPage = AppPage.Assets;
    [RelayCommand] private void NavigateToSettings()     => CurrentPage = AppPage.Settings;

    // ── Nastavení — Vzhled ─────────────────────────────────────
    [ObservableProperty]
    private bool _isDarkTheme = SettingsService.Current.IsDarkTheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAccentBlue))]
    [NotifyPropertyChangedFor(nameof(IsAccentPurple))]
    [NotifyPropertyChangedFor(nameof(IsAccentGreen))]
    [NotifyPropertyChangedFor(nameof(IsAccentOrange))]
    [NotifyPropertyChangedFor(nameof(IsAccentDarkGreen))]
    [NotifyPropertyChangedFor(nameof(IsAccentDarkBlue))]
    [NotifyPropertyChangedFor(nameof(IsAccentRed))]
    private AccentColor _selectedAccent =
        System.Enum.TryParse<AccentColor>(SettingsService.Current.Accent, out var a) ? a : AccentColor.Blue;

    public bool IsAccentBlue      => SelectedAccent == AccentColor.Blue;
    public bool IsAccentPurple    => SelectedAccent == AccentColor.Purple;
    public bool IsAccentGreen     => SelectedAccent == AccentColor.Green;
    public bool IsAccentOrange    => SelectedAccent == AccentColor.Orange;
    public bool IsAccentDarkGreen => SelectedAccent == AccentColor.DarkGreen;
    public bool IsAccentDarkBlue  => SelectedAccent == AccentColor.DarkBlue;
    public bool IsAccentRed       => SelectedAccent == AccentColor.Red;

    partial void OnIsDarkThemeChanged(bool value)
    {
        ThemeService.SetTheme(value ? AppTheme.Dark : AppTheme.Light);
        SettingsService.Current.IsDarkTheme = value;
        SettingsService.Save();
    }

    [RelayCommand] private void SetAccentBlue()      => ApplyAccent(AccentColor.Blue);
    [RelayCommand] private void SetAccentPurple()    => ApplyAccent(AccentColor.Purple);
    [RelayCommand] private void SetAccentGreen()     => ApplyAccent(AccentColor.Green);
    [RelayCommand] private void SetAccentOrange()    => ApplyAccent(AccentColor.Orange);
    [RelayCommand] private void SetAccentDarkGreen() => ApplyAccent(AccentColor.DarkGreen);
    [RelayCommand] private void SetAccentDarkBlue()  => ApplyAccent(AccentColor.DarkBlue);
    [RelayCommand] private void SetAccentRed()       => ApplyAccent(AccentColor.Red);

    private void ApplyAccent(AccentColor accent)
    {
        SelectedAccent = accent;
        ThemeService.SetAccent(accent);
        SettingsService.Current.Accent = accent.ToString();
        SettingsService.Save();
    }

    // ── Nastavení — Připojení ──────────────────────────────────
    [ObservableProperty] private string _serverUrl = SettingsService.Current.ServerUrl;
    [ObservableProperty] private bool _isTestingConnection;
    [ObservableProperty] private string? _connectionStatus;
    [ObservableProperty] private bool _connectionOk;

    partial void OnServerUrlChanged(string value)
    {
        SettingsService.Current.ServerUrl = value;
        SettingsService.Save();
        // Reset stavu při změně URL
        ConnectionStatus = null;
        ConnectionOk = false;
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task TestConnectionAsync()
    {
        IsTestingConnection = true;
        ConnectionStatus = null;
        try
        {
            using var http = new System.Net.Http.HttpClient
            {
                Timeout = System.TimeSpan.FromSeconds(5)
            };
            var response = await http.GetAsync(ServerUrl.TrimEnd('/') + "/health");
            ConnectionOk = response.IsSuccessStatusCode;
            ConnectionStatus = ConnectionOk
                ? $"Připojeno ✓  ({(int)response.StatusCode})"
                : $"Chyba {(int)response.StatusCode} – {response.ReasonPhrase}";
        }
        catch (System.Exception ex)
        {
            ConnectionOk = false;
            ConnectionStatus = ex is System.Net.Http.HttpRequestException
                ? "Server nedostupný"
                : ex.Message;
        }
        finally
        {
            IsTestingConnection = false;
        }
    }
}
