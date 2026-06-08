using System.Threading;
using System.Threading.Tasks;
using AssetMap.Avalonia.ViewModels;
using AssetMap.Repos.Sync;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AssetMap.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose && PendingQueue.HasPending && DataContext is MainViewModel vm)
        {
            // Zruš okamžité zavření — zobrazí shutdown overlay
            e.Cancel = true;

            vm.IsShuttingDown      = true;
            vm.IsSyncingOnShutdown = true;
            vm.ShutdownStatus      = "Synchronizuji neuložené změny…";

            // Pokus o sync (max 5 s)
            using var cts = new CancellationTokenSource(5_000);
            bool ok = await SyncService.TryFlushAsync(cts.Token);

            vm.IsSyncingOnShutdown = false;

            if (ok)
            {
                vm.ShutdownStatus = "Vše synchronizováno ✓  Zavírám…";
            }
            else
            {
                int remaining = PendingQueue.Count;
                vm.ShutdownStatus = remaining > 0
                    ? $"{remaining} změn uloženo lokálně — nahrají se při příštím startu."
                    : "Synchronizace dokončena ✓";
            }

            // Krátká prodleva aby uživatel viděl výsledek
            await Task.Delay(1_800);

            _allowClose = true;
            Close();
        }

        base.OnClosing(e);
    }
}
