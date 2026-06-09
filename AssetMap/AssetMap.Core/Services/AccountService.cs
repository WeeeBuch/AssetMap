using AssetMap.Core.Models;
using AssetMap.Database;
using AssetMap.Entities;
using AssetMap.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssetMap.Core.Services;

public class AccountService(
    AppDbContext         db,
    IPortfolioService    portfolio,
    IServiceScopeFactory scopeFactory)
    : IAccountService
{
    // ── GET ALL ───────────────────────────────────────────────────
    public async Task<List<AccountFullDto>> GetAllAsync(
        Guid userId, CancellationToken ct = default)
    {
        var accounts = await db.Accounts
            .Where(a => a.UserId == userId && !a.IsArchived)
            .Include(a => a.Holdings).ThenInclude(h => h.Asset)
            .Include(a => a.Transactions).ThenInclude(t => t.Asset)
            .OrderByDescending(a => a.Id) // nejnovější první
            .ToListAsync(ct);

        // SQLite nepodporuje APPLY — Take/OrderBy uvnitř Include je třeba provést v paměti
        foreach (var acc in accounts)
            acc.Transactions = acc.Transactions
                .OrderByDescending(t => t.Date)
                .Take(25)
                .ToList();

        var result = new List<AccountFullDto>(accounts.Count);
        foreach (var acc in accounts)
            result.Add(await BuildDtoAsync(acc, ct));
        return result;
    }

    // ── GET BY ID ─────────────────────────────────────────────────
    public async Task<AccountFullDto?> GetByIdAsync(
        Guid accountId, CancellationToken ct = default)
    {
        var acc = await db.Accounts
            .Where(a => a.Id == accountId)
            .Include(a => a.Holdings).ThenInclude(h => h.Asset)
            .Include(a => a.Transactions).ThenInclude(t => t.Asset)
            .FirstOrDefaultAsync(ct);
        if (acc is null) return null;
        acc.Transactions = acc.Transactions
            .OrderByDescending(t => t.Date).Take(25).ToList();
        return await BuildDtoAsync(acc, ct);
    }

    // ── CREATE ────────────────────────────────────────────────────
    public async Task<AccountFullDto> CreateAsync(
        Guid userId, CreateAccountRequest req, CancellationToken ct = default)
    {
        // Zajisti existenci Asset záznamu pro daný symbol
        var asset = await EnsureAssetAsync(req.AssetSymbol, req.AccountType, req.UsdPrice, ct);

        var account = new Account
        {
            Id          = Guid.NewGuid(),
            UserId      = userId,
            Name        = req.Name.Trim(),
            Institution = string.IsNullOrWhiteSpace(req.Institution) ? null : req.Institution.Trim(),
            AccountType = req.AccountType,
            BaseCurrency = asset.Symbol,
            IconColorHex = req.IconColorHex,
        };
        db.Accounts.Add(account);

        // Počáteční holding (pokud zadán startBalance)
        if (req.StartBalance > 0)
        {
            db.Holdings.Add(new Holding
            {
                Id        = Guid.NewGuid(),
                AccountId = account.Id,
                AssetId   = asset.Id,
                Quantity  = (decimal)req.StartBalance,
                CostBasis = (decimal)(req.StartBalance * req.UsdPrice),
            });
        }

        // Peněženka — vytvoř WatchedWallet záznam pokud byla zadána adresa
        if (!string.IsNullOrWhiteSpace(req.WalletAddress))
        {
            db.WatchedWallets.Add(new WatchedWallet
            {
                Id         = Guid.NewGuid(),
                AccountId  = account.Id,
                Address    = req.WalletAddress.Trim(),
                Network    = req.WalletNetwork.HasValue
                             ? (BlockchainNetwork)req.WalletNetwork.Value
                             : BlockchainNetwork.Bitcoin,
                SyncStatus = SyncStatus.Pending,
            });
        }

        await db.SaveChangesAsync(ct);

        // Okamžitý snapshot po vytvoření
        await portfolio.TakeSnapshotAsync(account.Id, ct);

        // Spusť blockchain sync asynchronně (fire & forget, vlastní scope)
        if (!string.IsNullOrWhiteSpace(req.WalletAddress))
        {
            var walletId = (await db.WatchedWallets
                .Where(w => w.AccountId == account.Id)
                .Select(w => w.Id)
                .FirstOrDefaultAsync(ct));

            if (walletId != Guid.Empty)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var sync = scope.ServiceProvider.GetRequiredService<IWalletSyncService>();
                        await sync.SyncWalletAsync(walletId);
                    }
                    catch { /* tichá chyba — sync retry při dalším cyklu */ }
                });
        }

        return (await GetByIdAsync(account.Id, ct))!;
    }

    // ── UPDATE ────────────────────────────────────────────────────
    public async Task<bool> UpdateAsync(
        Guid accountId, UpdateAccountRequest req, CancellationToken ct = default)
    {
        int rows = await db.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Name,         req.Name.Trim())
                .SetProperty(a => a.Institution,  string.IsNullOrWhiteSpace(req.Institution) ? null : req.Institution.Trim())
                .SetProperty(a => a.AccountType,  req.AccountType)
                .SetProperty(a => a.IconColorHex, req.IconColorHex),
            ct);
        return rows > 0;
    }

    // ── DELETE (soft archive) ─────────────────────────────────────
    public async Task<bool> DeleteAsync(Guid accountId, CancellationToken ct = default)
    {
        int rows = await db.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsArchived, true),
            ct);
        return rows > 0;
    }

    // ── Helpers ───────────────────────────────────────────────────

    private async Task<AccountFullDto> BuildDtoAsync(Account acc, CancellationToken ct)
    {
        // Aktuální zůstatek z Holdings
        double nativeBalance = acc.Holdings.Sum(h => (double)h.Quantity);

        // Asset symbol + USD cena
        var    holding  = acc.Holdings.FirstOrDefault();
        string symbol   = acc.BaseCurrency;
        double usdPrice = await GetUsdPriceAsync(symbol, acc.AccountType, ct);
        double valueUsd = nativeBalance * usdPrice;

        // Historie z PortfolioSnapshots
        double[] history = await portfolio.GetHistoryUsdAsync(acc.Id, 365, ct);

        // Pokud žádná historie, flat linie
        if (history.Length == 0)
            history = Enumerable.Repeat(valueUsd, 365).ToArray();

        var txDtos = acc.Transactions.Select(t => new TransactionDto
        {
            Id            = t.Id,
            Date          = t.Date,
            Type          = t.Type,
            AssetSymbol   = t.Asset?.Symbol ?? symbol,
            Quantity      = t.Quantity,
            PricePerUnit  = t.PricePerUnit,
            Note          = t.Note,
            FromAccountId = t.FromAccountId,
            ToAccountId   = t.ToAccountId,
            Fee           = t.Fee,
            Category      = t.Category,
        }).ToList();

        var wallet = await db.WatchedWallets.FirstOrDefaultAsync(w => w.AccountId == acc.Id, ct);

        return new AccountFullDto
        {
            Id                = acc.Id,
            Name              = acc.Name,
            AccountType       = acc.AccountType,
            Institution       = acc.Institution,
            IconColorHex      = acc.IconColorHex,
            BaseCurrency      = symbol,
            CurrentBalance    = nativeBalance,
            CurrentValueUsd   = valueUsd,
            BalanceHistoryUsd = history,
            RecentTransactions = txDtos,
            WalletAddress     = wallet?.Address,
            WalletSyncStatus  = wallet?.SyncStatus.ToString(),
        };
    }

    private async Task<double> GetUsdPriceAsync(
        string symbol, AccountType type, CancellationToken ct)
    {
        // Nejnovější PriceSnapshot pro daný symbol
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Symbol == symbol, ct);
        if (asset is null) return 1.0;

        var snap = await db.PriceSnapshots
            .Where(p => p.AssetId == asset.Id)
            .OrderByDescending(p => p.Timestamp)
            .FirstOrDefaultAsync(ct);

        return snap is not null ? (double)snap.Price : 1.0;
    }

    private async Task<Asset> EnsureAssetAsync(
        string symbol, AccountType accType, double usdPrice, CancellationToken ct)
    {
        string upperSymbol = symbol.Trim().ToUpperInvariant();
        // Normalize "KČ" → "CZK"
        if (upperSymbol == "KČ") upperSymbol = "CZK";

        var asset = await db.Assets.FirstOrDefaultAsync(
            a => a.Symbol == upperSymbol, ct);

        if (asset is null)
        {
            asset = new Asset
            {
                Id       = Guid.NewGuid(),
                Symbol   = upperSymbol,
                Name     = upperSymbol,
                AssetType = accType switch
                {
                    AccountType.CryptoWallet => AssetType.Crypto,
                    AccountType.Brokerage    => AssetType.Stock,
                    _                        => AssetType.Fiat,
                },
            };
            db.Assets.Add(asset);
        }

        // Ulož aktuální cenu jako snapshot
        if (usdPrice > 0)
        {
            db.PriceSnapshots.Add(new PriceSnapshot
            {
                Id        = Guid.NewGuid(),
                AssetId   = asset.Id,
                Price     = (decimal)usdPrice,
                Timestamp = DateTime.UtcNow,
            });
        }

        return asset;
    }
}
