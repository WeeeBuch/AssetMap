using AssetMap.Core.Models;
using AssetMap.Core.Services;
using AssetMap.Database;
using AssetMap.Entities;
using AssetMap.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;

namespace AssetMap.API.Controllers;

[ApiController]
[Route("api/accounts")]
public class AccountsController(IAccountService accounts, IImportService importer, AppDbContext db, IPortfolioService portfolio, IServiceScopeFactory scopeFactory) : ControllerBase
{
    /// <summary>Vrátí ID přihlášeného uživatele z kontextu (nastaven ApiKeyMiddleware).</summary>
    private Guid UserId => HttpContext.Items["UserId"] is Guid id ? id
        : Guid.Parse("00000000-0000-0000-0000-000000000001"); // fallback = seed user

    /// <summary>Všechny účty včetně celé historie.</summary>
    [HttpGet("full")]
    public async Task<IActionResult> GetFull(CancellationToken ct) =>
        Ok(await accounts.GetAllAsync(UserId, ct));

    /// <summary>Jeden účet.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var dto = await accounts.GetByIdAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>Přidá nový účet.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateAccountRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest("Name is required.");

        var dto = await accounts.CreateAsync(UserId, req, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Upraví metadata účtu (jméno, instituce, typ, barva).</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateAccountRequest req, CancellationToken ct)
    {
        bool ok = await accounts.UpdateAsync(id, req, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Archivuje (soft-delete) účet.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        bool ok = await accounts.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Importuje transakce z CSV souboru do daného účtu.
    /// Podporované formáty: generic (Date,Type,Amount,Note), Revolut, Trading212.
    /// </summary>
    [HttpPost("{id:guid}/import")]
    [RequestSizeLimit(10 * 1024 * 1024)] // max 10 MB
    public async Task<IActionResult> ImportCsv(
        Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Soubor je prázdný nebo nebyl odeslán.");

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Pouze CSV soubory jsou podporovány.");

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await importer.ImportCsvAsync(id, UserId, file.FileName, stream, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>Aktualizuje poznámku/popis transakce.</summary>
    [HttpPatch("transactions/{txId:guid}/note")]
    public async Task<IActionResult> UpdateTransactionNote(
        Guid txId, [FromBody] string? note, CancellationToken ct)
    {
        var tx = await db.Transactions.FindAsync([txId], ct);
        if (tx is null) return NotFound();
        tx.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Přidá manuální transakci k účtu.
    /// Typ Transfer vytvoří dvě transakce: Withdrawal na zdrojovém + Deposit na cílovém účtu.
    /// </summary>
    [HttpPost("{id:guid}/transactions")]
    public async Task<IActionResult> AddTransaction(
        Guid id, [FromBody] CreateTransactionRequest req, CancellationToken ct)
    {
        // Načti zdrojový účet
        var srcAccount = await db.Accounts
            .Include(a => a.Holdings).ThenInclude(h => h.Asset)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (srcAccount is null) return NotFound("Zdrojový účet nenalezen.");

        // Asset pro BaseCurrency
        var srcAsset = await db.Assets.FirstOrDefaultAsync(a => a.Symbol == srcAccount.BaseCurrency, ct);
        if (srcAsset is null) return BadRequest("Asset pro BaseCurrency nenalezen.");

        var now = DateTime.UtcNow;
        var txDate = req.Date == default ? now : DateTime.SpecifyKind(req.Date, DateTimeKind.Utc);
        var txsToSave = new List<Transaction>();

        if (req.ToAccountId is Guid toId)
        {
            // ── Transfer: výběr + vklad ───────────────────────────────
            var dstAccount = await db.Accounts
                .Include(a => a.Holdings).ThenInclude(h => h.Asset)
                .FirstOrDefaultAsync(a => a.Id == toId, ct);
            if (dstAccount is null) return NotFound("Cílový účet nenalezen.");
            var dstAsset = await db.Assets.FirstOrDefaultAsync(a => a.Symbol == dstAccount.BaseCurrency, ct);
            if (dstAsset is null) return BadRequest("Asset cílového účtu nenalezen.");

            string srcName = srcAccount.Name ?? "?";
            string dstName = dstAccount.Name ?? "?";

            var withdrawal = new Transaction
            {
                Id           = Guid.NewGuid(),
                AccountId    = id,
                Date         = txDate,
                Type         = TransactionType.Withdrawal,
                AssetId      = srcAsset.Id,
                Quantity     = (decimal)req.Amount,
                PricePerUnit = 1m,
                Fee          = req.Fee > 0 ? (decimal)req.Fee : null,
                ToAccountId  = toId,
                Note         = req.Note ?? $"Převod → {dstName}",
                Category     = req.Category,
            };
            var deposit = new Transaction
            {
                Id             = Guid.NewGuid(),
                AccountId      = toId,
                Date           = txDate,
                Type           = TransactionType.Deposit,
                AssetId        = dstAsset.Id,
                Quantity       = (decimal)req.Amount,
                PricePerUnit   = 1m,
                FromAccountId  = id,
                Note           = req.Note ?? $"Převod ← {srcName}",
                Category       = req.Category,
            };
            txsToSave.Add(withdrawal);
            txsToSave.Add(deposit);

            // Aktualizuj holdingy
            AdjustHolding(db, srcAccount, srcAsset, -(decimal)req.Amount - (decimal)req.Fee);
            AdjustHolding(db, dstAccount, dstAsset, (decimal)req.Amount);
        }
        else
        {
            // ── Příchozí / Odchozí ────────────────────────────────────
            decimal sign = req.Type == TransactionType.Deposit ? 1m : -1m;

            var tx = new Transaction
            {
                Id           = Guid.NewGuid(),
                AccountId    = id,
                Date         = txDate,
                Type         = req.Type,
                AssetId      = srcAsset.Id,
                Quantity     = (decimal)req.Amount,
                PricePerUnit = 1m,
                Fee          = req.Fee > 0 ? (decimal)req.Fee : null,
                Note         = req.Note,
                Category     = req.Category,
            };
            txsToSave.Add(tx);

            AdjustHolding(db, srcAccount, srcAsset, sign * (decimal)req.Amount - (decimal)req.Fee);
        }

        db.Transactions.AddRange(txsToSave);
        await db.SaveChangesAsync(ct);

        // Snapshot dnešního stavu
        await portfolio.TakeSnapshotAsync(id, ct);

        return Ok(txsToSave.Select(t => t.Id).ToArray());
    }

    /// <summary>Spustí manuální sync krypto peněženky (znovu načte transakce z blockchainu).</summary>
    [HttpPost("{id:guid}/sync-wallet")]
    public async Task<IActionResult> SyncWallet(Guid id, CancellationToken ct)
    {
        var wallet = await db.WatchedWallets.FirstOrDefaultAsync(w => w.AccountId == id, ct);
        if (wallet is null) return NotFound("Peněženka nenalezena.");

        // Reset na Pending aby sync proběhl znovu
        wallet.SyncStatus = SyncStatus.Pending;
        await db.SaveChangesAsync(ct);

        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<IWalletSyncService>();
                await sync.SyncWalletAsync(wallet.Id);
            }
            catch { /* tichá chyba */ }
        });

        return Accepted(new { message = "Sync zahájen." });
    }

    /// <summary>
    /// DEBUG: Testuje stejnou cestu jako skutečný sync.
    /// - single adresa → blockstream.info/txs (vrátí transakce)
    /// - xpub/zpub/ypub → NBitcoin derivace prvních 5 adres + blockstream.info check
    /// Neukládá nic do DB.
    /// </summary>
    [HttpGet("{id:guid}/debug-wallet")]
    public async Task<IActionResult> DebugWallet(Guid id, CancellationToken ct)
    {
        var wallet = await db.WatchedWallets.FirstOrDefaultAsync(w => w.AccountId == id, ct);
        if (wallet is null) return NotFound("Peněženka nenalezena.");

        string addr   = wallet.Address.Trim();
        bool   isXpub = addr.StartsWith("xpub", StringComparison.OrdinalIgnoreCase) ||
                        addr.StartsWith("ypub", StringComparison.OrdinalIgnoreCase) ||
                        addr.StartsWith("zpub", StringComparison.OrdinalIgnoreCase);

        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Add("User-Agent", "AssetMap/1.0");

        if (!isXpub)
        {
            // Single address → stejný endpoint jako sync
            string url = $"https://blockstream.info/api/address/{Uri.EscapeDataString(addr)}/txs";
            try
            {
                using var resp = await http.GetAsync(url, ct);
                string body    = await resp.Content.ReadAsStringAsync(ct);
                return Ok(new { mode = "single-addr", address = addr, url,
                    httpStatus = (int)resp.StatusCode,
                    body = body.Length > 3000 ? body[..3000] + "…" : body });
            }
            catch (Exception ex) { return Ok(new { mode = "single-addr", address = addr, url, error = ex.Message }); }
        }

        // xpub/ypub/zpub → ukáže prvních 10 odvozených adres + jejich tx počet
        try
        {
            // Stejná konverze jako ve WalletSyncService.ParseExtendedKey
            string xpubNorm = addr;
            string scriptDesc;
            if (addr.StartsWith("zpub", StringComparison.OrdinalIgnoreCase))
            {
                scriptDesc = "Segwit/bc1q";
                var data = NBitcoin.DataEncoders.Encoders.Base58Check.DecodeData(addr);
                new byte[] { 0x04, 0x88, 0xB2, 0x1E }.CopyTo(data, 0);
                xpubNorm = NBitcoin.DataEncoders.Encoders.Base58Check.EncodeData(data);
            }
            else if (addr.StartsWith("ypub", StringComparison.OrdinalIgnoreCase))
            {
                scriptDesc = "SegwitP2SH/3...";
                var data = NBitcoin.DataEncoders.Encoders.Base58Check.DecodeData(addr);
                new byte[] { 0x04, 0x88, 0xB2, 0x1E }.CopyTo(data, 0);
                xpubNorm = NBitcoin.DataEncoders.Encoders.Base58Check.EncodeData(data);
            }
            else { scriptDesc = "Legacy/1..."; }

            var extKey    = NBitcoin.ExtPubKey.Parse(xpubNorm, NBitcoin.Network.Main);
            var scriptType = addr.StartsWith("zpub", StringComparison.OrdinalIgnoreCase)
                ? NBitcoin.ScriptPubKeyType.Segwit
                : addr.StartsWith("ypub", StringComparison.OrdinalIgnoreCase)
                    ? NBitcoin.ScriptPubKeyType.SegwitP2SH
                    : NBitcoin.ScriptPubKeyType.Legacy;

            var results = new List<object>();
            for (int i = 0; i < 5; i++)
            {
                string derivedAddr = extKey.Derive(0u).Derive((uint)i)
                    .PubKey.GetAddress(scriptType, NBitcoin.Network.Main).ToString();

                string bsUrl = $"https://blockstream.info/api/address/{derivedAddr}/txs";
                try
                {
                    using var r = await http.GetAsync(bsUrl, ct);
                    string bsBody = await r.Content.ReadAsStringAsync(ct);
                    int txCount = bsBody.StartsWith("[") ? System.Text.Json.JsonDocument.Parse(bsBody).RootElement.GetArrayLength() : -1;
                    results.Add(new { index = i, address = derivedAddr, txCount,
                        httpStatus = (int)r.StatusCode });
                }
                catch (Exception ex) { results.Add(new { index = i, address = derivedAddr, error = ex.Message }); }
                await Task.Delay(100, ct);
            }

            return Ok(new { mode = "xpub", originalKey = addr[..20] + "…", scriptDesc,
                derivedAddresses = results,
                note = "Sync derivuje až 2000 adres (gap limit 20). Tady prvních 5 z external chain (m/0/i)." });
        }
        catch (Exception ex)
        {
            return Ok(new { mode = "xpub", error = $"Chyba parsování klíče: {ex.Message}" });
        }
    }

    private static void AdjustHolding(AppDbContext db, Account account, Asset asset, decimal delta)
    {
        var h = account.Holdings.FirstOrDefault(h => h.AssetId == asset.Id);
        if (h is null)
        {
            h = new AssetMap.Entities.Holding
            {
                Id        = Guid.NewGuid(),
                AccountId = account.Id,
                AssetId   = asset.Id,
                Quantity  = 0m,
                CostBasis = 0m,
            };
            db.Holdings.Add(h);
        }
        h.Quantity = Math.Max(0m, h.Quantity + delta);
    }
}
