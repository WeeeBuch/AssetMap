using AssetMap.Avalonia.Services;
using AssetMap.Avalonia.ViewModels;
using AssetMap.Avalonia.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AssetMap.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Aplikuj uložené nastavení před vytvořením oken
        var s = SettingsService.Current;
        ThemeService.SetTheme(s.IsDarkTheme ? AppTheme.Dark : AppTheme.Light);
        if (System.Enum.TryParse<AccentColor>(s.Accent, out var accent))
            ThemeService.SetAccent(accent);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var loginVm = new LoginViewModel();
            var loginWindow = new LoginWindow { DataContext = loginVm };

            loginVm.LoginSucceeded += () =>
            {
                var mainWindow = new MainWindow { DataContext = new MainViewModel() };
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                loginWindow.Close();
            };

            desktop.MainWindow = loginWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
