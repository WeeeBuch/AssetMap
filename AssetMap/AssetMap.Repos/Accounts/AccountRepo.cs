using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AssetMap.Entities;
using AssetMap.Entities.Enums;

namespace AssetMap.Repos.Accounts;

/// <summary>
/// Repozitář účtů — volá AssetMap.API přes HTTP.
/// Pokud server není dostupný, vrátí mock data (pro offline vývoj).
/// </summary>
public static class AccountRepo
{
    // ── Cache ─────────────────────────────────────────────────
    private static List<AccountData> _cache = [];
    public static IReadOnlyList<AccountData> GetAll() => _cache;

    /// <summary>Vyvolá se po každém úspěšném refresh (na UI vlákně).</summary>
    public static event Action? DataRefreshed;

    // ── Konfigurace serveru ───────────────────────────────────
    /// <summary>Adresa API serveru. Výchozí: localhost:5033.</summary>
    public static string ServerUrl { get; set; } = "http://localhost:5033";

    /// <summary>API klíč (prázdný = bez autentizace).</summary>
    public static string ApiKey { get; set; } = "";

    // ── Zobrazovací měna ──────────────────────────────────────
    public static string DisplayCurrency { get; private set; } = "USD";
    public static double UsdToDisplay    { get; private set; } = 1.0;

    public static void SetDisplayCurrency(string code, double usdToDisplay)
    {
        DisplayCurrency = code;
        UsdToDisplay    = usdToDisplay;
    }

    // ── HTTP klient ───────────────────────────────────────────
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static void ApplyAuthHeader()
    {
        _http.DefaultRequestHeaders.Remove("Authorization");
        if (!string.IsNullOrWhiteSpace(ApiKey))
            _http.DefaultRequestHeaders.Add("Authorization", $"ApiKey {ApiKey}");
    }

    // ── Kurzy pro symboly ─────────────────────────────────────
    public static double UsdRateForSymbol(string symbol) => symbol switch
    {
        "Kč" or "CZK" => FxRates.FiatToUsd("CZK"),
        "EUR"          => FxRates.FiatToUsd("EUR"),
        "USD"          => 1.0,
        "GBP"          => FxRates.FiatToUsd("GBP"),
        "CHF"          => FxRates.FiatToUsd("CHF"),
        "JPY"          => FxRates.FiatToUsd("JPY"),
        "PLN"          => FxRates.FiatToUsd("PLN"),
        "BTC"          => 83_000,
        "ETH"          => 3_200,
        "SOL"          => 170,
        "BNB"          => 600,
        _              => 1.0,
    };

    // ── JSON options ──────────────────────────────────────────
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // ── GET /api/accounts/full ────────────────────────────────
    public static async Task RefreshAsync()
    {
        try
        {
            ApplyAuthHeader();
            var resp = await _http.GetAsync($"{ServerUrl.TrimEnd('/')}/api/accounts/full");
            resp.EnsureSuccessStatusCode();

            var dtos = JsonSerializer.Deserialize<List<AccountFullDto>>(
                await resp.Content.ReadAsStringAsync(), _json) ?? [];

            _cache = dtos.Select(MapToAccountData).ToList();
            DataRefreshed?.Invoke();
        }
        catch
        {
            // Server nedostupný — zachovat stávající cache (nebo načíst mock při prvním spuštění)
            if (_cache.Count == 0)
            {
#if DEBUG
                _cache = GenerateMock();
#endif
            }
            DataRefreshed?.Invoke();
        }
    }

    public static IReadOnlyList<AccountData> EnsureLoaded()
    {
        if (_cache.Count == 0)
        {
#if DEBUG
            _cache = GenerateMock();
#endif
        }
        return _cache;
    }

    // ── POST /api/accounts ────────────────────────────────────
    public static async Task AddAccountAsync(
        string      name,
        string      institution,
        AccountType type,
        string      assetSymbol,
        double      startBalance,
        double      usdRateOverride = 0,
        string?     iconColorHex    = null)
    {
        double usdRate = usdRateOverride > 0 ? usdRateOverride : UsdRateForSymbol(assetSymbol);

        var req = new
        {
            name,
            institution,
            accountType  = (int)type,
            assetSymbol,
            startBalance,
            usdPrice     = usdRate,
            iconColorHex,
        };

        try
        {
            ApplyAuthHeader();
            var body = new StringContent(
                JsonSerializer.Serialize(req, _json),
                Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{ServerUrl.TrimEnd('/')}/api/accounts", body);
            resp.EnsureSuccessStatusCode();

            // Znovu načíst z API (aktualizuje cache)
            await RefreshAsync();
        }
        catch
        {
            // Fallback: přidat lokálně
            AddAccountLocal(name, institution, type, assetSymbol, startBalance, usdRate, iconColorHex);
        }
    }

    // Zachovat sync verzi pro zpětnou kompatibilitu s VM kódem
    public static void AddAccount(
        string      name,
        string      institution,
        AccountType type,
        string      assetSymbol,
        double      startBalance,
        double      usdRateOverride = 0,
        string?     iconColorHex    = null)
        => _ = AddAccountAsync(name, institution, type, assetSymbol, startBalance, usdRateOverride, iconColorHex);

    // ── PUT /api/accounts/{id} ────────────────────────────────
    public static void UpdateAccount(
        Guid        accountId,
        string      name,
        string      institution,
        AccountType type,
        string?     iconColorHex)
        => _ = UpdateAccountAsync(accountId, name, institution, type, iconColorHex);

    public static async Task UpdateAccountAsync(
        Guid        accountId,
        string      name,
        string      institution,
        AccountType type,
        string?     iconColorHex)
    {
        var req = new { name, institution, accountType = (int)type, iconColorHex };
        try
        {
            ApplyAuthHeader();
            var body = new StringContent(JsonSerializer.Serialize(req, _json), Encoding.UTF8, "application/json");
            var resp = await _http.PutAsync($"{ServerUrl.TrimEnd('/')}/api/accounts/{accountId}", body);
            resp.EnsureSuccessStatusCode();
            await RefreshAsync();
        }
        catch
        {
            UpdateAccountLocal(accountId, name, institution, type, iconColorHex);
        }
    }

    // ── DELETE /api/accounts/{id} ─────────────────────────────
    public static void RemoveAccount(Guid accountId)
        => _ = RemoveAccountAsync(accountId);

    public static async Task RemoveAccountAsync(Guid accountId)
    {
        try
        {
            ApplyAuthHeader();
            var resp = await _http.DeleteAsync($"{ServerUrl.TrimEnd('/')}/api/accounts/{accountId}");
            resp.EnsureSuccessStatusCode();
            await RefreshAsync();
        }
        catch
        {
            _cache.RemoveAll(d => d.Account.Id == accountId);
            DataRefreshed?.Invoke();
        }
    }

    // ── POST /api/transactions ────────────────────────────────
    public static void AddTransaction(
        Guid            accountId,
        TransactionType type,
        double          amount,
        DateTime        date,
        string?         note = null)
        => _ = AddTransactionAsync(accountId, type, amount, date, note);

    public static async Task AddTransactionAsync(
        Guid            accountId,
        TransactionType type,
        double          amount,
        DateTime        date,
        string?         note = null)
    {
        var req = new
        {
            accountId,
            type       = (int)type,
            amount,
            date       = date.ToString("o"),
            note,
        };
        try
        {
            ApplyAuthHeader();
            var body = new StringContent(JsonSerializer.Serialize(req, _json), Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{ServerUrl.TrimEnd('/')}/api/transactions", body);
            resp.EnsureSuccessStatusCode();
            await RefreshAsync();
        }
        catch
        {
            AddTransactionLocal(accountId, type, amount, date, note);
        }
    }

    // ── Mapping: DTO → AccountData ────────────────────────────
    private static AccountData MapToAccountData(AccountFullDto dto)
    {
        var account = new Account
        {
            Id          = dto.Id,
            UserId      = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name        = dto.Name,
            Institution = dto.Institution,
            AccountType = dto.AccountType,
            IconColorHex = dto.IconColorHex,
            BaseCurrency = dto.BaseCurrency,
        };

        var txs = dto.RecentTransactions.Select(t =>
        {
            // Server posílá jen AssetSymbol, ne AssetId — vygenerujeme lokální GUID
            var assetId = SymbolToGuid(t.AssetSymbol);
            var asset = new Asset
            {
                Id        = assetId,
                Symbol    = t.AssetSymbol,
                Name      = t.AssetSymbol,
                AssetType = account.AccountType == AccountType.CryptoWallet
                            ? AssetType.Crypto : AssetType.Fiat,
            };
            return new Transaction
            {
                Id           = t.Id,
                AccountId    = dto.Id,
                Date         = t.Date,
                Type         = t.Type,
                AssetId      = assetId,
                Asset        = asset,
                Quantity     = (decimal)t.Quantity,
                PricePerUnit = (decimal)t.PricePerUnit,
                Note         = t.Note,
            };
        }).ToList();

        return new AccountData
        {
            Account           = account,
            IconColorHex      = dto.IconColorHex,
            BaseCurrency      = dto.BaseCurrency,
            CurrentBalance    = dto.CurrentBalance,
            ConvertedCurrency = DisplayCurrency,
            ConvertedBalance  = dto.CurrentValueUsd * UsdToDisplay,
            BalanceHistory    = dto.BalanceHistoryUsd, // vždy v USD
            RecentTransactions = txs,
        };
    }

    // ── DTO typy (zrcadlí AssetMap.Core.Models) ───────────────
    private class AccountFullDto
    {
        public Guid        Id               { get; set; }
        public string      Name             { get; set; } = "";
        public AccountType AccountType      { get; set; }
        public string?     Institution      { get; set; }
        public string?     IconColorHex     { get; set; }
        public string      BaseCurrency     { get; set; } = "";
        public double      CurrentBalance   { get; set; }
        public double      CurrentValueUsd  { get; set; }
        public double[]    BalanceHistoryUsd { get; set; } = [];
        public List<TransactionDto> RecentTransactions { get; set; } = [];
    }

    private class TransactionDto
    {
        public Guid            Id           { get; set; }
        public DateTime        Date         { get; set; }
        public TransactionType Type         { get; set; }
        public string          AssetSymbol  { get; set; } = "";
        public double          Quantity     { get; set; }
        public double          PricePerUnit { get; set; }
        public string?         Note         { get; set; }
    }

    /// <summary>Deterministický GUID z názvu symbolu — stabilní přes refreshe.</summary>
    private static Guid SymbolToGuid(string symbol)
    {
        // Použijeme SHA-1-like přístup přes prostý hash
        int hash = string.GetHashCode(symbol, StringComparison.OrdinalIgnoreCase);
        // Rozbalíme int do 16 bajtů
        Span<byte> bytes = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, hash);
        return new Guid(bytes);
    }

    // ── Lokální fallback operace (bez serveru) ─────────────────
    private static void AddAccountLocal(
        string name, string institution, AccountType type,
        string assetSymbol, double startBalance, double usdRate, string? iconColorHex)
    {
        var data = new AccountData
        {
            Account = new Account
            {
                Id = Guid.NewGuid(), UserId = _mockUserId,
                Name = name, Institution = institution, AccountType = type,
                BaseCurrency = assetSymbol, IconColorHex = iconColorHex,
            },
            IconColorHex      = iconColorHex,
            BaseCurrency      = assetSymbol,
            CurrentBalance    = startBalance,
            ConvertedCurrency = DisplayCurrency,
            ConvertedBalance  = startBalance * usdRate * UsdToDisplay,
            BalanceHistory    = Enumerable.Repeat(startBalance * usdRate, 365).ToArray(),
            RecentTransactions = [],
        };
        _cache.Insert(0, data);
        DataRefreshed?.Invoke();
    }

    private static void UpdateAccountLocal(
        Guid accountId, string name, string institution, AccountType type, string? iconColorHex)
    {
        int idx = _cache.FindIndex(d => d.Account.Id == accountId);
        if (idx < 0) return;
        var old = _cache[idx];
        old.Account.Name        = name;
        old.Account.Institution = institution;
        old.Account.AccountType = type;
        old.Account.IconColorHex = iconColorHex;
        _cache[idx] = old with { IconColorHex = iconColorHex };
        DataRefreshed?.Invoke();
    }

    private static void AddTransactionLocal(
        Guid accountId, TransactionType type, double amount, DateTime date, string? note)
    {
        int idx = _cache.FindIndex(d => d.Account.Id == accountId);
        if (idx < 0) return;
        var data     = _cache[idx];
        var asset    = data.RecentTransactions.Count > 0
            ? data.RecentTransactions[0].Asset
            : new Asset { Id = Guid.NewGuid(), Symbol = data.BaseCurrency, Name = data.BaseCurrency, AssetType = AssetType.Fiat };
        var tx       = new Transaction
        {
            Id = Guid.NewGuid(), AccountId = accountId, Date = date, Type = type,
            AssetId = asset.Id, Asset = asset, Quantity = (decimal)amount, PricePerUnit = 1m, Note = note,
        };
        bool   dep        = type == TransactionType.Deposit;
        double newBalance = Math.Max(0, data.CurrentBalance + (dep ? amount : -amount));
        double usdRate    = UsdRateForSymbol(data.BaseCurrency);
        var    txList     = new List<Transaction> { tx };
        txList.AddRange(data.RecentTransactions);
        _cache[idx] = data with
        {
            CurrentBalance    = newBalance,
            ConvertedBalance  = newBalance * usdRate * UsdToDisplay,
            RecentTransactions = txList.Take(25).ToList(),
        };
        DataRefreshed?.Invoke();
    }

    // ── Mock user ID ──────────────────────────────────────────
    private static readonly Guid _mockUserId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

#if DEBUG
    // ── Mock data (fallback při nedostupném serveru) ───────────
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
        Build("Běžný účet",   "Česká spořitelna", AccountType.Bank,         AsstCzk, startBalance: 42_500,  seed: 1),
        Build("Spořicí účet", "Raiffeisenbank",   AccountType.Bank,         AsstCzk, startBalance: 185_000, seed: 2),
        Build("Revolut",      "Revolut",           AccountType.Bank,         AsstEur, startBalance: 2_400,   seed: 3),
        Build("Bitstamp",     "Bitcoin",           AccountType.CryptoWallet, AsstBtc, startBalance: 0.85,    seed: 4),
    ];

    private static AccountData Build(
        string name, string institution, AccountType type,
        Asset asset, double startBalance, int seed)
    {
        var rng     = new Random(seed);
        var accountId = Guid.NewGuid();
        var now     = DateTime.Today;
        string fxCode  = asset.Symbol.Equals("Kč", StringComparison.OrdinalIgnoreCase) ? "CZK" : asset.Symbol;
        double convRate = UsdRateForSymbol(asset.Symbol);
        bool hasFx  = PriceSnapshotRepo.TryGetHistory(fxCode, out double[] fxHistory) && fxHistory.Length == 365;

        var history = new double[365];
        var transactions = new List<Transaction>();
        double balance = startBalance;
        for (int i = 0; i < 365; i++)
        {
            double delta = (rng.NextDouble() - 0.44) * startBalance * 0.025;
            balance = Math.Max(balance + delta, startBalance * 0.1);
            history[i] = balance * (hasFx ? fxHistory[i] : convRate);
            if (rng.NextDouble() < 0.15)
            {
                bool   credit = rng.NextDouble() > 0.45;
                double amount = startBalance * (0.01 + rng.NextDouble() * 0.06);
                transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(), AccountId = accountId,
                    Date = now.AddDays(i - 364),
                    Type = credit ? TransactionType.Deposit : TransactionType.Withdrawal,
                    AssetId = asset.Id, Asset = asset,
                    Quantity = (decimal)amount, PricePerUnit = 1m,
                });
            }
        }

        double finalNative = balance;
        return new AccountData
        {
            Account = new Account
            {
                Id = accountId, UserId = _mockUserId,
                Name = name, Institution = institution, AccountType = type,
                BaseCurrency = asset.Symbol,
                Transactions = transactions,
            },
            IconColorHex      = null,
            BaseCurrency      = asset.Symbol,
            CurrentBalance    = finalNative,
            ConvertedCurrency = DisplayCurrency,
            ConvertedBalance  = finalNative * convRate * UsdToDisplay,
            BalanceHistory    = history,
            RecentTransactions = [.. transactions.OrderByDescending(t => t.Date).Take(25)],
        };
    }
#endif
}
