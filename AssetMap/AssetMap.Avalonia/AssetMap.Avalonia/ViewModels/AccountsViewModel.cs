using System.Collections.ObjectModel;
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

    // ── Měna pro přepočet (TODO: načíst z API) ────────────────
    [ObservableProperty] private string _displayCurrency = "USD";

    // ── Init ──────────────────────────────────────────────────
    public AccountsViewModel()
    {
        LoadMockAccounts();
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

    // ── Přidat účet ───────────────────────────────────────────
    [RelayCommand]
    private void AddAccount()
    {
        // TODO: otevřít dialog pro přidání účtu
    }

    // ── Mock data ─────────────────────────────────────────────
    private void LoadMockAccounts()
    {
        var accounts = new[]
        {
            AccountCardViewModel.CreateMock(
                "Běžný účet", "Česká spořitelna", "Č", "#0044EE",
                42_500, "Kč", "EUR", 0.040, seed: 1),

            AccountCardViewModel.CreateMock(
                "Spořicí účet", "Raiffeisenbank", "R", "#FF6B00",
                185_000, "Kč", "EUR", 0.040, seed: 2),

            AccountCardViewModel.CreateMock(
                "Revolut", "Revolut", "V", "#C44DFF",
                2_400, "EUR", "USD", 1.085, seed: 3),

            AccountCardViewModel.CreateMock(
                "Bitstamp", "Bitcoin", "₿", "#F7931A",
                0.85, "BTC", "USD", 82_000, seed: 4),
        };

        foreach (var acc in accounts)
        {
            acc.OnSelect = a => SelectAccount(a);
            Accounts.Add(acc);
        }
    }
}
