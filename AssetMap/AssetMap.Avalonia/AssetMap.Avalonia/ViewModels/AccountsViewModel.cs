using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AssetMap.Repos.Accounts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetMap.Avalonia.ViewModels;

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
    public PieSliceData[] PieSlices   { get; private set; } = [];
    public double[]       TotalHistory { get; private set; } = [];
    public ChartLine[]    AccountLines { get; private set; } = [];

    // Toggle: celkový součet vs. jednotlivé čáry
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnTotalChart))]
    private bool _isOnAccountsChart = false;

    public bool IsOnTotalChart => !IsOnAccountsChart;

    [RelayCommand] private void SwitchToTotal()    => IsOnAccountsChart = false;
    [RelayCommand] private void SwitchToAllLines() => IsOnAccountsChart = true;

    // ── Měna pro přepočet (TODO: načíst z API) ────────────────
    [ObservableProperty] private string _displayCurrency = "USD";

    // ── Init ──────────────────────────────────────────────────
    public AccountsViewModel()
    {
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

        // Kliknutí na již vybraný = zavřít detail
        if (SelectedAccount == account)
        {
            SelectedAccount = null;
            return;
        }

        SelectedAccount = account;
        account.IsSelected = true;
    }

    // ── Zavřít detail ────────────────────────────────────────
    [RelayCommand]
    private void CloseDetail()
    {
        if (SelectedAccount is not null)
            SelectedAccount.IsSelected = false;
        SelectedAccount = null;
    }

    // ── Přidat účet ───────────────────────────────────────────
    [RelayCommand]
    private void AddAccount()
    {
        // TODO: otevřít dialog pro přidání účtu
    }

    // ── Načtení dat z repo ────────────────────────────────────
    private void LoadAccounts(System.Collections.Generic.IReadOnlyList<AccountData> data)
    {
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

        // Normalizovaná hodnota = zůstatek × konverzní kurz
        double[] normalized = accs
            .Select(a => a.RawBalance * a.ConversionRate)
            .ToArray();
        double totalNorm = normalized.Sum();

        // ── Koláčový graf ─────────────────────────────────────
        PieSlices = accs.Select((a, i) => new PieSliceData
        {
            Label      = a.AccountName,
            Value      = normalized[i],
            Percent    = (totalNorm > 0 ? normalized[i] / totalNorm * 100 : 0).ToString("N1") + " %",
            SliceBrush = a.IconBrush,
        }).ToArray();

        // ── Celková linie (součet normalizovaných historií) ────
        int len = accs[0].BalanceHistory.Length; // všechny mají stejnou délku (60)
        TotalHistory = Enumerable.Range(0, len)
            .Select(i => accs.Sum(a => a.BalanceHistory[i] * a.ConversionRate))
            .ToArray();

        // ── Čáry pro jednotlivé účty (% změna od začátku) ─────
        // Normalizace na % změnu → všechny čáry jsou srovnatelné
        AccountLines = accs.Select(a =>
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
    }
}
