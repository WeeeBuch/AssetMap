using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AssetMap.Repos.Accounts;
using Avalonia.Threading;
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
    public PieSliceData[] PieSlices { get; private set; } = [];

    // Raw (plná délka) — interně
    private double[]     _rawTotalHistory  = [];
    private ChartLine[]  _rawAccountLines  = [];

    // Slicované podle periody — bindovatelné
    private double[]    _totalHistory  = [];
    private ChartLine[] _accountLines  = [];

    public double[] TotalHistory
    {
        get => _totalHistory;
        private set { _totalHistory = value; OnPropertyChanged(); }
    }

    public ChartLine[] AccountLines
    {
        get => _accountLines;
        private set { _accountLines = value; OnPropertyChanged(); }
    }

    // ── Toggle: celkový součet vs. jednotlivé čáry ────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnTotalChart))]
    private bool _isOnAccountsChart = false;

    public bool IsOnTotalChart => !IsOnAccountsChart;

    [RelayCommand] private void SwitchToTotal()    => IsOnAccountsChart = false;
    [RelayCommand] private void SwitchToAllLines() => IsOnAccountsChart = true;

    // ── Perioda grafu ─────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPeriodD1))]
    [NotifyPropertyChangedFor(nameof(IsPeriodD7))]
    [NotifyPropertyChangedFor(nameof(IsPeriodM1))]
    [NotifyPropertyChangedFor(nameof(IsPeriodM3))]
    [NotifyPropertyChangedFor(nameof(IsPeriodY1))]
    private ChartPeriod _chartPeriod = ChartPeriod.M3;

    public bool IsPeriodD1 => ChartPeriod == ChartPeriod.D1;
    public bool IsPeriodD7 => ChartPeriod == ChartPeriod.D7;
    public bool IsPeriodM1 => ChartPeriod == ChartPeriod.M1;
    public bool IsPeriodM3 => ChartPeriod == ChartPeriod.M3;
    public bool IsPeriodY1 => ChartPeriod == ChartPeriod.Y1;

    [RelayCommand] private void SetPeriodD1() => ChartPeriod = ChartPeriod.D1;
    [RelayCommand] private void SetPeriodD7() => ChartPeriod = ChartPeriod.D7;
    [RelayCommand] private void SetPeriodM1() => ChartPeriod = ChartPeriod.M1;
    [RelayCommand] private void SetPeriodM3() => ChartPeriod = ChartPeriod.M3;
    [RelayCommand] private void SetPeriodY1() => ChartPeriod = ChartPeriod.Y1;

    partial void OnChartPeriodChanged(ChartPeriod value) => ApplyPeriod();

    private void ApplyPeriod()
    {
        int take = ChartPeriod switch
        {
            ChartPeriod.D1 => Math.Min(2,   _rawTotalHistory.Length),
            ChartPeriod.D7 => Math.Min(7,   _rawTotalHistory.Length),
            ChartPeriod.M1 => Math.Min(30,  _rawTotalHistory.Length),
            ChartPeriod.M3 => Math.Min(90,  _rawTotalHistory.Length),
            ChartPeriod.Y1 => _rawTotalHistory.Length,
            _              => _rawTotalHistory.Length
        };
        if (take < 2) take = 2;

        TotalHistory = _rawTotalHistory[^take..];
        AccountLines = _rawAccountLines
            .Select(l => new ChartLine
            {
                Label     = l.Label,
                Values    = l.Values.Length >= take ? l.Values[^take..] : l.Values,
                LineBrush = l.LineBrush,
            })
            .ToArray();
    }

    // ── Měna pro přepočet (TODO: načíst z API) ────────────────
    [ObservableProperty] private string _displayCurrency = "EUR";

    // ── Init ──────────────────────────────────────────────────
    public AccountsViewModel()
    {
        // Přihlásit se na event z repozitáře (price refresh každou hodinu)
        // TODO: odhlásit při dispose, pokud VM bude mít kratší životnost než App
        AccountRepo.DataRefreshed += () =>
            Dispatcher.UIThread.Post(() => LoadAccounts(AccountRepo.GetAll()));

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

        if (SelectedAccount == account)
        {
            SelectedAccount = null;
            return;
        }

        SelectedAccount = account;
        account.IsSelected = true;
    }

    // ── Zavřít detail ─────────────────────────────────────────
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
        // TODO: dialog
    }

    // ── Načtení dat z repo ────────────────────────────────────
    private void LoadAccounts(IReadOnlyList<AccountData> data)
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

        double[] normalized = accs.Select(a => a.RawBalance * a.ConversionRate).ToArray();
        double   totalNorm  = normalized.Sum();

        // Koláčový graf
        PieSlices = accs.Select((a, i) => new PieSliceData
        {
            Label      = a.AccountName,
            Value      = normalized[i],
            Percent    = (totalNorm > 0 ? normalized[i] / totalNorm * 100 : 0).ToString("N1") + " %",
            SliceBrush = a.IconBrush,
        }).ToArray();

        // Celková linie (raw — plná délka)
        int len = accs[0].BalanceHistory.Length;
        _rawTotalHistory = Enumerable.Range(0, len)
            .Select(i => accs.Sum(a => a.BalanceHistory[i] * a.ConversionRate))
            .ToArray();

        // Čáry jednotlivých účtů v % změně od začátku (raw)
        _rawAccountLines = accs.Select(a =>
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

        ApplyPeriod();
    }
}
