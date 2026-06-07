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

    // Server URL
    [ObservableProperty] private string _serverUrl = "http://localhost:5000";
    [ObservableProperty] private bool _isEditingServerUrl;

    [RelayCommand]
    private void ToggleEditServerUrl() => IsEditingServerUrl = !IsEditingServerUrl;

    [RelayCommand]
    private void ConfirmServerUrl() => IsEditingServerUrl = false;

    public event Action? LoginSucceeded;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            // TODO: volání API
            await Task.Delay(300);

            if (Username == "demo" && Password == "demo")
                LoginSucceeded?.Invoke();
            else
                ErrorMessage = "Neplatné přihlašovací údaje.";
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
