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
