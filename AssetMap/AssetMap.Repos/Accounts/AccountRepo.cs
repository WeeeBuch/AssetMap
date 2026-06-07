using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AssetMap.Entities;
using AssetMap.Entities.Enums;

namespace AssetMap.Repos.Accounts;

/// <summary>
/// Repozitář účtů s in-memory cache.
/// Volat RefreshAsync() pro načtení ze serveru (zatím mock data).
/// </summary>
public static class AccountRepo
{
    // ── Cache ─────────────────────────────────────────────────
    private static List<AccountData> _cache = [];

    public static IReadOnlyList<AccountData> GetAll() => _cache;

    // ── API volání ────────────────────────────────────────────
    /// <summary>
    /// Načte data ze serveru a uloží do cache.
    /// TODO: nahradit HTTP voláním na API endpoint /api/accounts/full
    /// </summary>
    public static Task RefreshAsync()
    {
        _cache = GenerateMock();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Vrátí cache; pokud je prázdná, načte mock (sync fallback pro init).
    /// </summary>
    public static IReadOnlyList<AccountData> EnsureLoaded()
    {
        if (_cache.Count == 0)
            _cache = GenerateMock();
        return _cache;
    }

#if DEBUG
    // ── Mock data ─────────────────────────────────────────────
    // Statické assety (v produkci přijdou ze serveru)
    private static readonly Guid _userId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Asset AsstCzk = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
        Symbol = "Kč", Name = "Česká koruna", AssetType = AssetType.Fiat,
    };
    private static readonly Asset AsstEur = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
        Symbol = "EUR", Name = "Euro", AssetType = AssetType.Fiat,
    };
    private static readonly Asset AsstBtc = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000012"),
        Symbol = "BTC", Name = "Bitcoin", AssetType = AssetType.Crypto,
    };

    private static List<AccountData> GenerateMock() =>
    [
        Build("Běžný účet",    "Česká spořitelna", AccountType.Bank,         AsstCzk,
              startBalance: 42_500,  convertedCurrency: "EUR", conversionRate: 0.040, seed: 1),

        Build("Spořicí účet",  "Raiffeisenbank",   AccountType.Bank,         AsstCzk,
              startBalance: 185_000, convertedCurrency: "EUR", conversionRate: 0.040, seed: 2),

        Build("Revolut",       "Revolut",           AccountType.Bank,         AsstEur,
              startBalance: 2_400,   convertedCurrency: "USD", conversionRate: 1.085, seed: 3),

        Build("Bitstamp",      "Bitcoin",           AccountType.CryptoWallet, AsstBtc,
              startBalance: 0.85,    convertedCurrency: "USD", conversionRate: 82_000, seed: 4),
    ];

    private static AccountData Build(
        string name, string institution, AccountType type,
        Asset asset, double startBalance,
        string? convertedCurrency, double conversionRate,
        int seed)
    {
        var rng       = new Random(seed);
        var accountId = Guid.NewGuid();
        var now       = DateTime.Today;

        var history      = new double[60];
        var transactions = new List<Transaction>();
        double balance   = startBalance;

        for (int i = 0; i < 60; i++)
        {
            double delta = (rng.NextDouble() - 0.44) * startBalance * 0.025;
            balance      = Math.Max(balance + delta, startBalance * 0.1);
            history[i]   = balance;

            // ~15 % šance na transakci
            if (rng.NextDouble() < 0.15)
            {
                bool   credit = rng.NextDouble() > 0.45;
                double amount = startBalance * (0.01 + rng.NextDouble() * 0.06);

                transactions.Add(new Transaction
                {
                    Id           = Guid.NewGuid(),
                    AccountId    = accountId,
                    Date         = now.AddDays(i - 59),
                    Type         = credit ? TransactionType.Deposit : TransactionType.Withdrawal,
                    AssetId      = asset.Id,
                    Asset        = asset,
                    Quantity     = (decimal)amount,
                    PricePerUnit = 1m,   // fiat 1:1; pro BTC by server doplnil cenu
                });
            }
        }

        double final = history[^1];

        return new AccountData
        {
            Account = new Account
            {
                Id           = accountId,
                UserId       = _userId,
                Name         = name,
                Institution  = institution,
                AccountType  = type,
                Transactions = transactions,
            },
            BaseCurrency      = asset.Symbol,
            CurrentBalance    = final,
            ConvertedCurrency = convertedCurrency,
            ConvertedBalance  = final * conversionRate,
            BalanceHistory    = history,
            RecentTransactions = [.. transactions.OrderByDescending(t => t.Date).Take(25)],
        };
    }

#endif

}
