using System;
using System.Net.Http;
using System.Threading.Tasks;
using AssetMap.Avalonia.Services;
using AssetMap.Repos.Accounts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetMap.Avalonia.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    // ── Server URL ─────────────────────────────────────────
    [ObservableProperty] private string _serverUrl = SettingsService.Current.ServerUrl;
    [ObservableProperty] private bool   _isEditingServerUrl;

    [RelayCommand] private void ToggleEditServerUrl() => IsEditingServerUrl = !IsEditingServerUrl;

    [RelayCommand]
    private void ConfirmServerUrl()
    {
        IsEditingServerUrl = false;
        SettingsService.Current.ServerUrl = ServerUrl;
        SettingsService.Save();
        AccountRepo.ServerUrl = ServerUrl;
    }

    // ── API klíč ───────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _apiKey = SettingsService.Current.ApiKey;

    // ── Stav ──────────────────────────────────────────────
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool    _isBusy;

    public event Action? LoginSucceeded;

    // ── Přihlášení ────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        IsBusy       = true;
        ErrorMessage = null;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

            // Přidej API klíč pokud je zadán
            if (!string.IsNullOrWhiteSpace(ApiKey))
                http.DefaultRequestHeaders.Add("Authorization", $"ApiKey {ApiKey.Trim()}");

            var resp = await http.GetAsync(ServerUrl.TrimEnd('/') + "/health");

            if (resp.IsSuccessStatusCode)
            {
                // Ulož nastavení
                SettingsService.Current.ApiKey    = ApiKey.Trim();
                SettingsService.Current.ServerUrl = ServerUrl;
                SettingsService.Save();

                // Nastav do repozitáře
                AccountRepo.ServerUrl = ServerUrl;
                AccountRepo.ApiKey    = ApiKey.Trim();

                LoginSucceeded?.Invoke();
            }
            else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                ErrorMessage = "Neplatný API klíč.";
            }
            else
            {
                ErrorMessage = $"Server vrátil chybu {(int)resp.StatusCode}.";
            }
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Server není dostupný. Zkontroluj URL a jestli server běží.";
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = "Vypršel časový limit (6 s). Server neodpovídá.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Přihlásit lze vždy (i prázdný klíč = dev mód serveru bez auth)
    private bool CanLogin() => !IsBusy;

    // ── Offline (přeskočit přihlášení) ────────────────────
    [RelayCommand]
    private void ContinueOffline()
    {
        // Nastav uložené hodnoty, přihlas bez serveru
        AccountRepo.ServerUrl = ServerUrl;
        AccountRepo.ApiKey    = SettingsService.Current.ApiKey;
        LoginSucceeded?.Invoke();
    }
}
