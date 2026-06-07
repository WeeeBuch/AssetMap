using AssetMap.Avalonia.Services;
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

    [RelayCommand] private void NavigateToDashboard()    => CurrentPage = AppPage.Dashboard;
    [RelayCommand] private void NavigateToAccounts()     => CurrentPage = AppPage.Accounts;
    [RelayCommand] private void NavigateToTransactions() => CurrentPage = AppPage.Transactions;
    [RelayCommand] private void NavigateToAssets()       => CurrentPage = AppPage.Assets;
    [RelayCommand] private void NavigateToSettings()     => CurrentPage = AppPage.Settings;

    // ── Nastavení — Vzhled ─────────────────────────────────────
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private AccentColor _selectedAccent = AccentColor.Blue;

    partial void OnIsDarkThemeChanged(bool value)
        => ThemeService.SetTheme(value ? AppTheme.Dark : AppTheme.Light);

    [RelayCommand] private void SetAccentBlue()   => ApplyAccent(AccentColor.Blue);
    [RelayCommand] private void SetAccentPurple() => ApplyAccent(AccentColor.Purple);
    [RelayCommand] private void SetAccentGreen()  => ApplyAccent(AccentColor.Green);
    [RelayCommand] private void SetAccentOrange() => ApplyAccent(AccentColor.Orange);

    private void ApplyAccent(AccentColor accent)
    {
        SelectedAccent = accent;
        ThemeService.SetAccent(accent);
    }

    // ── Nastavení — Připojení ──────────────────────────────────
    [ObservableProperty] private string _serverUrl = "http://localhost:5000";
    [ObservableProperty] private bool _isTestingConnection;
    [ObservableProperty] private string? _connectionStatus;
    [ObservableProperty] private bool _connectionOk;

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
                ? "Připojeno ✓"
                : $"Chyba: {(int)response.StatusCode}";
        }
        catch
        {
            ConnectionOk = false;
            ConnectionStatus = "Server nedostupný";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }
}
