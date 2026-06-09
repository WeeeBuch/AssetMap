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
using AssetMap.Repos.Sync;

namespace AssetMap.Repos.Accounts;

/// <summary>
/// Repozitář účtů — volá AssetMap.API přes HTTP (optimistic local-first).
/// Offline: mutace se ukládají do PendingQueue a synchonizují se při připojení.
/// </summary>
public static class AccountRepo
{
    // ── Cache ─────────────────────────────────────────────
    private static List<AccountData> _cache = [];
    public static IReadOnlyList<AccountData> GetAll() => _cache;

    /// <summary>Vyvolá se po každé změně cache (na libovolném vlákně — UI použije Dispatcher).</summary>
    public static event Action? DataRefreshed;

    // ── Konfigurace serveru ───────────────────────────────
    public static string ServerUrl
    {
        get => _serverUrl;
        set
        {
            _serverUrl = value;
            // Propagovat do SyncService
            SyncService.GetServerUrl = () => _serverUrl;
        }
    }
    private static string _serverUrl = "http://localhost:5033";

    public static string ApiKey
    {
        get => _apiKey;
        set
        {
            _apiKey = value;
            SyncService.GetApiKey = () => _apiKey;
        }
    }
    private static string _apiKey = "";

    // ── Zobrazovací měna ──────────────────────────────────
    public static string DisplayCurrency { get; private set; } = "USD";
    public static double UsdToDisplay    { get; private set; } = 1.0;

    public static void SetDisplayCurrency(string code, double usdToDisplay)
    {
        DisplayCurrency = code;
        UsdToDisplay    = usdToDisplay;
    }

    // ── HTTP klient ───────────────────────────────────────
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static void ApplyAuthHeader()
    {
        _http.DefaultRequestHeaders.Remove("Authorization");
        if (!string.IsNullOrWhiteSpace(_apiKey))
            _http.DefaultRequestHeaders.Add("Authorization", $"ApiKey {_apiKey}");
    }

    // ── Kurzy pro symboly ─────────────────────────────────
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

    // ── JSON options ──────────────────────────────────────
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // ── GET /api/accounts/full ────────────────────────────
    /// <summary>
    /// Načte účty ze serveru a uloží do cache.
    /// Pokud server nedostupný: načte z LocalCacheService.
    /// </summary>
    public static async Task RefreshAsync()
    {
        try
        {
            ApplyAuthHeader();
            var resp = await _http.GetAsync($"{_serverUrl.TrimEnd('/')}/api/accounts/full");
            resp.EnsureSuccessStatusCode();

            string json = await resp.Content.ReadAsStringAsync();

            // Uložit na disk (offline fallback)
            LocalCacheService.SaveRaw(json);

            var dtos = JsonSerializer.Deserialize<List<AccountFullDto>>(json, _json) ?? [];
            _cache = dtos.Select(d => MapToAccountData(d, isPending: false)).ToList();

            // Přidat zpět pending položky (lokálně vytvořené, ještě nesynced)
            MergePendingAccounts();

            SyncService.SetOnline(true);
            DataRefreshed?.Invoke();
        }
        catch
        {
            // Server nedostupný — zkusit lokální cache
            if (_cache.Count == 0)
                LoadFromLocalCache();

            DataRefreshed?.Invoke();
        }
    }

    private static void LoadFromLocalCache()
    {
        string? json = LocalCacheService.LoadRaw();
        if (json is null)
        {
#if DEBUG
            _cache = GenerateMock();
#endif
            return;
        }

        try
        {
            var dtos = JsonSerializer.Deserialize<List<AccountFullDto>>(json, _json) ?? [];
            _cache = dtos.Select(d => MapToAccountData(d, isPending: false)).ToList();
            MergePendingAccounts();
        }
        catch
        {
#if DEBUG
            _cache = GenerateMock();
#endif
        }
    }

    /// <summary>
    /// Přidá do cache zpět lokálně vytvořené (IsPending) účty, které jsou stále ve frontě.
    /// Zabrání jejich zmizení po RefreshAsync.
    /// </summary>
    private static void MergePendingAccounts()
    {
        foreach (var mutation in PendingQueue.GetAll())
        {
            if (mutation.Type != MutationType.CreateAccount) continue;
            if (mutation.LocalAccountId is not { } localId) continue;

            // Pokud server data již obsahují tento účet (po sync), nepřidávej
            if (_cache.Any(d => d.Account.Id == localId)) continue;

            // Rekonstruuj AccountData z uloženého payloadu
            try
            {
                var req = JsonSerializer.Deserialize<CreateAccountPayload>(mutation.Payload, _json);
                if (req is null) continue;

                // Use current client rate for known assets (same fix as MapToAccountData)
                double usdRate = UsdRateForSymbol(req.AssetSymbol);
                bool   sameCurrPend = string.Equals(NormalizeSym(req.AssetSymbol),
                                         NormalizeSym(DisplayCurrency),
                                         StringComparison.OrdinalIgnoreCase);
                double pendConv = sameCurrPend ? req.StartBalance : req.StartBalance * usdRate * UsdToDisplay;
                double pendUsd  = sameCurrPend && UsdToDisplay > 0
                                      ? req.StartBalance / UsdToDisplay
                                      : req.StartBalance * usdRate;
                _cache.Add(new AccountData
                {
                    Account = new Account
                    {
                        Id           = localId,
                        UserId       = _mockUserId,
                        Name         = req.Name,
                        Institution  = req.Institution,
                        AccountType  = (AccountType)req.AccountType,
                        BaseCurrency = req.AssetSymbol,
                        IconColorHex = req.IconColorHex,
                    },
                    IconColorHex      = req.IconColorHex,
                    BaseCurrency      = req.AssetSymbol,
                    CurrentBalance    = req.StartBalance,
                    ConvertedCurrency = DisplayCurrency,
                    ConvertedBalance  = pendConv,
                    BalanceHistory    = Enumerable.Repeat(pendUsd, 365).ToArray(),
                    RecentTransactions = [],
                    IsPending         = true,
                });
            }
            catch { }
        }
    }

    public static IReadOnlyList<AccountData> EnsureLoaded()
    {
        if (_cache.Count == 0)
            LoadFromLocalCache();
        return _cache;
    }

    // ── POST /api/accounts ────────────────────────────────
    public static async Task AddAccountAsync(
        string      name,
        string      institution,
        AccountType type,
        string      assetSymbol,
        double      startBalance,
        double      usdRateOverride = 0,
        string?     iconColorHex    = null,
        string?     walletAddress   = null,
        int?        walletNetwork   = null)
    {
        double usdRate   = usdRateOverride > 0 ? usdRateOverride : UsdRateForSymbol(assetSymbol);
        var    localId   = Guid.NewGuid();
        var    payload   = new CreateAccountPayload
        {
            Name          = name,
            Institution   = institution,
            AccountType   = (int)type,
            AssetSymbol   = assetSymbol,
            StartBalance  = startBalance,
            UsdPrice      = usdRate,
            IconColorHex  = iconColorHex,
            WalletAddress = walletAddress,
            WalletNetwork = walletNetwork,
        };
        string payloadJson = JsonSerializer.Serialize(payload, _json);

        // 1. Optimistická lokální aktualizace (okamžitě viditelné v UI)
        bool sameCurrOpt = string.Equals(NormalizeSym(assetSymbol), NormalizeSym(DisplayCurrency),
                                         StringComparison.OrdinalIgnoreCase);
        double optConvBalance = sameCurrOpt ? startBalance : startBalance * usdRate * UsdToDisplay;
        double optUsd         = sameCurrOpt && UsdToDisplay > 0
                                    ? startBalance / UsdToDisplay
                                    : startBalance * usdRate;
        _cache.Add(new AccountData
        {
            Account = new Account
            {
                Id = localId, UserId = _mockUserId,
                Name = name, Institution = institution, AccountType = type,
                BaseCurrency = assetSymbol, IconColorHex = iconColorHex,
            },
            IconColorHex      = iconColorHex,
            BaseCurrency      = assetSymbol,
            CurrentBalance    = startBalance,
            ConvertedCurrency = DisplayCurrency,
            ConvertedBalance  = optConvBalance,
            BalanceHistory    = Enumerable.Repeat(optUsd, 365).ToArray(),
            RecentTransactions = [],
            IsPending         = true,
        });
        DataRefreshed?.Invoke();

        // 2. Uložit do fronty (přetrvává přes restart)
        var mutation = new PendingMutation
        {
            Type           = MutationType.CreateAccount,
            HttpMethod     = "POST",
            Endpoint       = "/api/accounts",
            Payload        = payloadJson,
            LocalAccountId = localId,
        };
        PendingQueue.Enqueue(mutation);

        // 3. Pokus o okamžité odeslání
        await TrySendAndDequeue(mutation);
    }

    public static void AddAccount(
        string name, string institution, AccountType type,
        string assetSymbol, double startBalance,
        double usdRateOverride = 0, string? iconColorHex = null,
        string? walletAddress = null, int? walletNetwork = null)
        => _ = AddAccountAsync(name, institution, type, assetSymbol, startBalance,
               usdRateOverride, iconColorHex, walletAddress, walletNetwork);

    // ── PUT /api/accounts/{id} ────────────────────────────
    public static void UpdateAccount(
        Guid accountId, string name, string institution, AccountType type, string? iconColorHex)
        => _ = UpdateAccountAsync(accountId, name, institution, type, iconColorHex);

    public static async Task UpdateAccountAsync(
        Guid accountId, string name, string institution, AccountType type, string? iconColorHex)
    {
        // Pending účty (bez server ID) — jen lokální update, skip HTTP
        var existing = _cache.FirstOrDefault(d => d.Account.Id == accountId);
        if (existing?.IsPending == true)
        {
            UpdateAccountLocal(accountId, name, institution, type, iconColorHex);
            return;
        }

        // 1. Optimistická lokální aktualizace
        UpdateAccountLocal(accountId, name, institution, type, iconColorHex);

        // 2. Fronta + okamžitý pokus
        var payloadObj = new { name, institution, accountType = (int)type, iconColorHex };
        var mutation = new PendingMutation
        {
            Type       = MutationType.UpdateAccount,
            HttpMethod = "PUT",
            Endpoint   = $"/api/accounts/{accountId}",
            Payload    = JsonSerializer.Serialize(payloadObj, _json),
        };
        PendingQueue.Enqueue(mutation);
        await TrySendAndDequeue(mutation);
    }

    // ── DELETE /api/accounts/{id} ─────────────────────────
    public static void RemoveAccount(Guid accountId)
        => _ = RemoveAccountAsync(accountId);

    public static async Task RemoveAccountAsync(Guid accountId)
    {
        // Pending účty — smaž jen lokálně + zruš CreateAccount mutaci z fronty
        var existing = _cache.FirstOrDefault(d => d.Account.Id == accountId);
        if (existing?.IsPending == true)
        {
            _cache.RemoveAll(d => d.Account.Id == accountId);
            // Zruš pending create mutaci
            foreach (var m in PendingQueue.GetAll())
            {
                if (m.LocalAccountId == accountId)
                    PendingQueue.Remove(m.Id);
            }
            DataRefreshed?.Invoke();
            return;
        }

        // 1. Optimistická lokální aktualizace
        _cache.RemoveAll(d => d.Account.Id == accountId);
        DataRefreshed?.Invoke();

        // 2. Fronta + okamžitý pokus
        var mutation = new PendingMutation
        {
            Type       = MutationType.DeleteAccount,
            HttpMethod = "DELETE",
            Endpoint   = $"/api/accounts/{accountId}",
            Payload    = "",
        };
        PendingQueue.Enqueue(mutation);
        await TrySendAndDequeue(mutation);
    }

    // ── POST /api/transactions ────────────────────────────
    public static void AddTransaction(
        Guid accountId, TransactionType type, double amount, DateTime date, string? note = null)
        => _ = AddTransactionAsync(accountId, type, amount, date, note);

    public static async Task AddTransactionAsync(
        Guid accountId, TransactionType type, double amount, DateTime date, string? note = null)
    {
        // 1. Optimistická lokální aktualizace
        AddTransactionLocal(accountId, type, amount, date, note);

        // Pending účet — neposlat transakci na server dokud účet neexistuje
        var existing = _cache.FirstOrDefault(d => d.Account.Id == accountId);
        if (existing?.IsPending == true)
            return;

        // 2. Fronta + okamžitý pokus
        var payloadObj = new
        {
            accountId,
            type       = (int)type,
            amount,
            date       = date.ToString("o"),
            note,
        };
        var mutation = new PendingMutation
        {
            Type       = MutationType.CreateTransaction,
            HttpMethod = "POST",
            Endpoint   = "/api/transactions",
            Payload    = JsonSerializer.Serialize(payloadObj, _json),
        };
        PendingQueue.Enqueue(mutation);
        await TrySendAndDequeue(mutation);
    }

    // ── POST /api/accounts/{id}/import ───────────────────
    /// <summary>
    /// Odešle CSV soubor na server k importu transakcí.
    /// Vrátí výsledek nebo null pokud server nedostupný.
    /// </summary>
    public static async Task<ImportCsvResult?> ImportCsvAsync(
        Guid accountId, string fileName, byte[] csvBytes)
    {
        try
        {
            ApplyAuthHeader();
            using var content = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(csvBytes);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
            content.Add(fileContent, "file", fileName);

            var resp = await _http.PostAsync(
                $"{_serverUrl.TrimEnd('/')}/api/accounts/{accountId}/import", content);

            if (!resp.IsSuccessStatusCode)
                return new ImportCsvResult
                {
                    Success      = false,
                    ErrorMessage = $"Server vrátil {(int)resp.StatusCode}: {resp.ReasonPhrase}",
                };

            var json   = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ImportCsvResult>(json, _json);
            if (result is not null)
            {
                result.Success = true;
                await RefreshAsync();
            }
            return result;
        }
        catch (Exception ex)
        {
            return new ImportCsvResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public class ImportCsvResult
    {
        public bool   Success       { get; set; }
        public string? ErrorMessage  { get; set; }
        public string DetectedFormat { get; set; } = "";
        public int    TotalRows     { get; set; }
        public int    SuccessCount  { get; set; }
        public int    ErrorCount    { get; set; }
    }

    // ── PATCH /api/accounts/transactions/{id}/note ────────
    /// <summary>Aktualizuje poznámku transakce na serveru.</summary>
    public static async Task<bool> UpdateTransactionNoteAsync(Guid transactionId, string? note)
    {
        try
        {
            ApplyAuthHeader();
            var json    = System.Text.Json.JsonSerializer.Serialize(note);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var resp    = await _http.PatchAsync(
                $"{_serverUrl.TrimEnd('/')}/api/accounts/transactions/{transactionId}/note",
                content);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── POST /api/accounts/{id}/transactions ─────────────
    /// <summary>
    /// Přidá manuální transakci. Typ Transfer vytvoří dvě transakce (výběr + vklad).
    /// Vrátí true pokud server odpověděl úspěšně.
    /// </summary>
    public static async Task<bool> AddManualTransactionAsync(
        Guid accountId,
        AssetMap.Entities.Enums.TransactionType type,
        double amount,
        double fee,
        DateTime date,
        string? note,
        Guid? toAccountId = null,
        Guid? fromAccountId = null,
        string? category = null)
    {
        try
        {
            ApplyAuthHeader();
            var payload = new
            {
                accountId,
                type         = (int)type,
                amount,
                fee,
                date,
                note,
                toAccountId,
                fromAccountId,
                category,
            };
            var json    = System.Text.Json.JsonSerializer.Serialize(payload, _json);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var resp    = await _http.PostAsync(
                $"{_serverUrl.TrimEnd('/')}/api/accounts/{accountId}/transactions", content);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Parsování počátečního zůstatku z CSV ──────────────
    /// <summary>
    /// Pokusí se přečíst počáteční zůstatek z CSV výpisu.
    /// Aktuálně podporuje KB+ formát (řádek "Pocatecni zustatek;40470,07;...").
    /// Vrátí null pokud formát neznámý nebo číslo nelze přečíst.
    /// </summary>
    public static double? ParseCsvInitialBalance(byte[] csvBytes)
    {
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var utf8  = System.Text.Encoding.UTF8.GetString(csvBytes);
            var text  = utf8.Contains('?')
                ? System.Text.Encoding.GetEncoding(1250).GetString(csvBytes)
                : utf8;

            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith("Pocatecni zustatek;", StringComparison.OrdinalIgnoreCase))
                {
                    var cols = line.Split(';');
                    if (cols.Length > 1 &&
                        double.TryParse(cols[1].Replace(',', '.').Trim(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double v))
                        return v;
                }
            }
            return null;
        }
        catch { return null; }
    }

    // ── Shared: send + dequeue logic ──────────────────────
    private static async Task TrySendAndDequeue(PendingMutation mutation)
    {
        try
        {
            ApplyAuthHeader();
            string baseUrl = _serverUrl.TrimEnd('/');
            HttpResponseMessage resp = mutation.HttpMethod switch
            {
                "DELETE" => await _http.DeleteAsync(baseUrl + mutation.Endpoint),
                "PUT"    => await _http.PutAsync(
                                baseUrl + mutation.Endpoint,
                                new StringContent(mutation.Payload, Encoding.UTF8, "application/json")),
                _        => await _http.PostAsync(
                                baseUrl + mutation.Endpoint,
                                new StringContent(mutation.Payload, Encoding.UTF8, "application/json")),
            };

            if (resp.IsSuccessStatusCode)
            {
                PendingQueue.Remove(mutation.Id);
                // Znovu načíst ze serveru (real IDs, snapshots atd.)
                await RefreshAsync();
            }
            // else: mutation zůstane ve frontě, SyncService retry
        }
        catch
        {
            // Server nedostupný — mutation zůstane ve frontě
        }
    }

    // ── Mapping: DTO → AccountData ────────────────────────
    private static AccountData MapToAccountData(AccountFullDto dto, bool isPending)
    {
        var account = new Account
        {
            Id           = dto.Id,
            UserId       = _mockUserId,
            Name         = dto.Name,
            Institution  = dto.Institution,
            AccountType  = dto.AccountType,
            IconColorHex = dto.IconColorHex,
            BaseCurrency = dto.BaseCurrency,
        };

        var txs = dto.RecentTransactions.Select(t =>
        {
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
                Id            = t.Id,
                AccountId     = dto.Id,
                Date          = t.Date,
                Type          = t.Type,
                AssetId       = assetId,
                Asset         = asset,
                Quantity      = (decimal)t.Quantity,
                PricePerUnit  = (decimal)t.PricePerUnit,
                Note          = t.Note,
                FromAccountId = t.FromAccountId,
                ToAccountId   = t.ToAccountId,
                Fee           = t.Fee.HasValue ? (decimal)t.Fee.Value : null,
                Category      = t.Category,
            };
        }).ToList();

        // ── Recompute display value using CLIENT's live rates ──────
        // Server's CurrentValueUsd may use a stale PriceSnapshot (from account creation)
        // that differs from the client's current UsdToDisplay → causes e.g. 44k vs 50k CZK.
        //
        // Strategy:
        //  • Same currency (CZK account, display = CZK): ConvertedBalance = CurrentBalance exactly.
        //  • Known asset (fiat, major crypto): recompute from native × current client rate.
        //  • Unknown asset: fall back to server's CurrentValueUsd (we have no local price).
        string sym = dto.BaseCurrency;
        bool sameCurrency = string.Equals(NormalizeSym(sym), NormalizeSym(DisplayCurrency),
                                          StringComparison.OrdinalIgnoreCase);

        double currentUsd;
        double convertedBalance;

        if (sameCurrency)
        {
            // Exact — no FX round-trip needed
            convertedBalance = dto.CurrentBalance;
            // Approximate USD for history scaling: native / UsdToDisplay
            currentUsd = UsdToDisplay > 0 ? dto.CurrentBalance / UsdToDisplay : dto.CurrentValueUsd;
        }
        else
        {
            double usdRate     = UsdRateForSymbol(sym);
            // usdRate == 1.0 and symbol ≠ USD means no local price → trust server
            bool   useServer   = usdRate == 1.0 && !string.Equals(sym, "USD",
                                     StringComparison.OrdinalIgnoreCase);
            currentUsd     = useServer ? dto.CurrentValueUsd : dto.CurrentBalance * usdRate;
            convertedBalance = currentUsd * UsdToDisplay;
        }

        // Scale history so its last point matches currentUsd (keeps chart shapes correct)
        double histScale = dto.CurrentValueUsd > 0 ? currentUsd / dto.CurrentValueUsd : 1.0;
        double[] history = dto.BalanceHistoryUsd.Length > 0
            ? dto.BalanceHistoryUsd.Select(v => v * histScale).ToArray()
            : dto.BalanceHistoryUsd;

        return new AccountData
        {
            Account           = account,
            IconColorHex      = dto.IconColorHex,
            BaseCurrency      = dto.BaseCurrency,
            CurrentBalance    = dto.CurrentBalance,
            ConvertedCurrency = DisplayCurrency,
            ConvertedBalance  = convertedBalance,
            BalanceHistory    = history,
            RecentTransactions = txs,
            IsPending         = isPending,
        };
    }

    /// <summary>Normalizes asset symbol for currency comparison (Kč → CZK, uppercase).</summary>
    private static string NormalizeSym(string s) =>
        string.Equals(s, "Kč", StringComparison.OrdinalIgnoreCase) ? "CZK" : s.ToUpperInvariant();

    // ── DTO typy (zrcadlí AssetMap.Core.Models) ───────────
    private class AccountFullDto
    {
        public Guid        Id                { get; set; }
        public string      Name              { get; set; } = "";
        public AccountType AccountType       { get; set; }
        public string?     Institution       { get; set; }
        public string?     IconColorHex      { get; set; }
        public string      BaseCurrency      { get; set; } = "";
        public double      CurrentBalance    { get; set; }
        public double      CurrentValueUsd   { get; set; }
        public double[]    BalanceHistoryUsd { get; set; } = [];
        public List<TransactionDto> RecentTransactions { get; set; } = [];
    }

    private class TransactionDto
    {
        public Guid            Id            { get; set; }
        public DateTime        Date          { get; set; }
        public TransactionType Type          { get; set; }
        public string          AssetSymbol   { get; set; } = "";
        public double          Quantity      { get; set; }
        public double          PricePerUnit  { get; set; }
        public string?         Note          { get; set; }
        public Guid?           FromAccountId { get; set; }
        public Guid?           ToAccountId   { get; set; }
        public double?         Fee           { get; set; }
        public string?         Category      { get; set; }
    }

    /// <summary>Zrcadlí tělo POST /api/accounts pro uložení do fronty.</summary>
    private class CreateAccountPayload
    {
        public string  Name          { get; set; } = "";
        public string  Institution   { get; set; } = "";
        public int     AccountType   { get; set; }
        public string  AssetSymbol   { get; set; } = "";
        public double  StartBalance  { get; set; }
        public double  UsdPrice      { get; set; }
        public string? IconColorHex  { get; set; }
        public string? WalletAddress { get; set; }
        public int?    WalletNetwork { get; set; }
    }

    // ── Lokální fallback operace (bez serveru) ─────────────
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
        var data  = _cache[idx];
        var asset = data.RecentTransactions.Count > 0
            ? data.RecentTransactions[0].Asset
            : new Asset
              {
                  Id = Guid.NewGuid(), Symbol = data.BaseCurrency,
                  Name = data.BaseCurrency, AssetType = AssetType.Fiat
              };
        var tx = new Transaction
        {
            Id = Guid.NewGuid(), AccountId = accountId, Date = date, Type = type,
            AssetId = asset.Id, Asset = asset,
            Quantity = (decimal)amount, PricePerUnit = 1m, Note = note,
        };
        bool   dep        = type == TransactionType.Deposit;
        double newBalance = Math.Max(0, data.CurrentBalance + (dep ? amount : -amount));
        double usdRate    = UsdRateForSymbol(data.BaseCurrency);
        var    txList     = new List<Transaction> { tx };
        txList.AddRange(data.RecentTransactions);
        _cache[idx] = data with
        {
            CurrentBalance     = newBalance,
            ConvertedBalance   = newBalance * usdRate * UsdToDisplay,
            RecentTransactions = txList.Take(25).ToList(),
        };
        DataRefreshed?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────
    private static readonly Guid _mockUserId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>Deterministický GUID z názvu symbolu — stabilní přes refreshe.</summary>
    private static Guid SymbolToGuid(string symbol)
    {
        int hash = string.GetHashCode(symbol, StringComparison.OrdinalIgnoreCase);
        Span<byte> bytes = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, hash);
        return new Guid(bytes);
    }

#if DEBUG
    // ── Mock data (fallback při nedostupném serveru bez cache) ─
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
        BuildMock("Běžný účet",   "Česká spořitelna", AccountType.Bank,         AsstCzk, 42_500,  1),
        BuildMock("Spořicí účet", "Raiffeisenbank",   AccountType.Bank,         AsstCzk, 185_000, 2),
        BuildMock("Revolut",      "Revolut",           AccountType.Bank,         AsstEur, 2_400,   3),
        BuildMock("Bitstamp",     "Bitcoin",           AccountType.CryptoWallet, AsstBtc, 0.85,    4),
    ];

    private static AccountData BuildMock(
        string name, string institution, AccountType type,
        Asset asset, double startBalance, int seed)
    {
        var rng       = new Random(seed);
        var accountId = Guid.NewGuid();
        var now       = DateTime.Today;
        string fxCode  = asset.Symbol.Equals("Kč", StringComparison.OrdinalIgnoreCase) ? "CZK" : asset.Symbol;
        double convRate = UsdRateForSymbol(asset.Symbol);
        bool hasFx    = PriceSnapshotRepo.TryGetHistory(fxCode, out double[] fxHistory) && fxHistory.Length == 365;

        var history      = new double[365];
        var transactions = new List<Transaction>();
        double balance   = startBalance;
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

        return new AccountData
        {
            Account = new Account
            {
                Id = accountId, UserId = _mockUserId,
                Name = name, Institution = institution, AccountType = type,
                BaseCurrency = asset.Symbol, Transactions = transactions,
            },
            IconColorHex      = null,
            BaseCurrency      = asset.Symbol,
            CurrentBalance    = balance,
            ConvertedCurrency = DisplayCurrency,
            ConvertedBalance  = balance * convRate * UsdToDisplay,
            BalanceHistory    = history,
            RecentTransactions = [.. transactions.OrderByDescending(t => t.Date).Take(25)],
            IsPending         = false,
        };
    }
#endif
}
