using AssetMap.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace AssetMap.Avalonia.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    // Server URL — načte se z uloženého nastavení
    [ObservableProperty] private string _serverUrl = SettingsService.Current.ServerUrl;
    [ObservableProperty] private bool _isEditingServerUrl;

    [RelayCommand]
    private void ToggleEditServerUrl() => IsEditingServerUrl = !IsEditingServerUrl;

    [RelayCommand]
    private void ConfirmServerUrl()
    {
        IsEditingServerUrl = false;
        SettingsService.Current.ServerUrl = ServerUrl;
        SettingsService.Save();
    }

    public event Action? LoginSucceeded;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            (bool res, ErrorMessage) = Repos.Auth.Login.TryLogin(Username, Password);

            if (res) LoginSucceeded?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLogin() =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !IsBusy;
}
