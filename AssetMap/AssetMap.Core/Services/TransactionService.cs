using AssetMap.Core.Models;
using AssetMap.Database;
using AssetMap.Entities;
using AssetMap.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace AssetMap.Core.Services;

public class TransactionService(AppDbContext db, IPortfolioService portfolio) : ITransactionService
{
    public async Task<TransactionDto> AddAsync(
        CreateTransactionRequest req, CancellationToken ct = default)
    {
        var account = await db.Accounts
            .Include(a => a.Holdings).ThenInclude(h => h.Asset)
            .FirstOrDefaultAsync(a => a.Id == req.AccountId, ct)
            ?? throw new InvalidOperationException($"Account {req.AccountId} not found.");

        // Najdi nebo vytvoř Asset pro BaseCurrency tohoto účtu
        var asset = await db.Assets.FirstOrDefaultAsync(
            a => a.Symbol == account.BaseCurrency, ct);
        asset ??= new Asset
        {
            Id       = Guid.NewGuid(),
            Symbol   = account.BaseCurrency,
            Name     = account.BaseCurrency,
            AssetType = account.AccountType == AccountType.CryptoWallet
                        ? AssetType.Crypto : AssetType.Fiat,
        };
        if (asset.Id == Guid.Empty) db.Assets.Add(asset);

        var tx = new Transaction
        {
            Id           = Guid.NewGuid(),
            AccountId    = req.AccountId,
            Date         = req.Date,
            Type         = req.Type,
            AssetId      = asset.Id,
            Asset        = asset,
            Quantity     = (decimal)Math.Abs(req.Amount),
            PricePerUnit = 1m,
            Note         = req.Note,
        };
        db.Transactions.Add(tx);

        // Aktualizuj Holding
        var holding = account.Holdings.FirstOrDefault(h => h.AssetId == asset.Id);
        if (holding is null)
        {
            holding = new Holding
            {
                Id        = Guid.NewGuid(),
                AccountId = req.AccountId,
                AssetId   = asset.Id,
                Quantity  = 0m,
                CostBasis = 0m,
            };
            db.Holdings.Add(holding);
        }

        bool isDeposit = req.Type == TransactionType.Deposit;
        holding.Quantity = Math.Max(0m,
            holding.Quantity + (isDeposit ? (decimal)req.Amount : -(decimal)req.Amount));

        await db.SaveChangesAsync(ct);

        // Snapshot po transakci
        await portfolio.TakeSnapshotAsync(req.AccountId, ct);

        return new TransactionDto
        {
            Id           = tx.Id,
            Date         = tx.Date,
            Type         = tx.Type,
            AssetSymbol  = asset.Symbol,
            Quantity     = tx.Quantity,
            PricePerUnit = tx.PricePerUnit,
            Note         = tx.Note,
        };
    }

    public async Task<List<TransactionDto>> GetForAccountAsync(
        Guid accountId, int limit = 50, CancellationToken ct = default)
    {
        return await db.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.Date)
            .Take(limit)
            .Select(t => new TransactionDto
            {
                Id           = t.Id,
                Date         = t.Date,
                Type         = t.Type,
                AssetSymbol  = t.Asset.Symbol,
                Quantity     = t.Quantity,
                PricePerUnit = t.PricePerUnit,
                Note         = t.Note,
            })
            .ToListAsync(ct);
    }
}
