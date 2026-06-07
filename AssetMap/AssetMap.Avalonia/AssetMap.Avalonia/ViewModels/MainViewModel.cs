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

    // Oddělený VM pro Accounts (má vlastní kolekci + selected state)
    public AccountsViewModel AccountsVM { get; } = new();

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
