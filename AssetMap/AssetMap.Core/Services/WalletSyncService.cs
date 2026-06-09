using System.Net.Http;
using System.Text.Json;
using AssetMap.Database;
using AssetMap.Entities;
using AssetMap.Entities.Enums;
using EfTransaction = AssetMap.Entities.Transaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace AssetMap.Core.Services;

/// <summary>
/// Synchronizuje transakce krypto peněženek z veřejných blockchain API.
/// Bitcoin: blockchain.info (bez API klíče)
/// Ethereum: Etherscan public API (bez API klíče, rate-limited)
/// </summary>
public class WalletSyncService(
    AppDbContext                  db,
    IPortfolioService             portfolio,
    ILogger<WalletSyncService>    logger)
    : IWalletSyncService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "AssetMap/1.0 (portfolio tracker)" } },
    };

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Public API ───────────────────────────────────────────
    public async Task SyncPendingWalletsAsync(CancellationToken ct = default)
    {
        var wallets = await db.WatchedWallets
            .Where(w => w.SyncStatus == SyncStatus.Pending)
            .Include(w => w.Account)
            .ToListAsync(ct);

        logger.LogInformation("Blockchain sync: {Count} pending wallet(s)", wallets.Count);

        foreach (var wallet in wallets)
            await SyncCoreAsync(wallet, ct);
    }

    public async Task SyncWalletAsync(Guid walletId, CancellationToken ct = default)
    {
        var wallet = await db.WatchedWallets
            .Include(w => w.Account)
            .FirstOrDefaultAsync(w => w.Id == walletId, ct);

        if (wallet is null) return;
        await SyncCoreAsync(wallet, ct);
    }

    // ── Core dispatcher ──────────────────────────────────────
    private async Task SyncCoreAsync(WatchedWallet wallet, CancellationToken ct)
    {
        logger.LogInformation("Syncing wallet {Address} ({Network})", wallet.Address, wallet.Network);
        try
        {
            bool ok = wallet.Network switch
            {
                BlockchainNetwork.Bitcoin  => await SyncBitcoinAsync(wallet, ct),
                BlockchainNetwork.Ethereum => await SyncEthereumAsync(wallet, ct),
                BlockchainNetwork.Litecoin => await SyncBlockCypherAsync(wallet, "ltc", ct),
                BlockchainNetwork.Cosmos   => await SyncCosmosAsync(wallet, ct),
                _ => false,
            };

            wallet.SyncStatus    = ok ? SyncStatus.Ok : SyncStatus.Error;
            wallet.LastSyncedAt  = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Wallet sync failed: {Address}", wallet.Address);
            wallet.SyncStatus = SyncStatus.Error;
        }

        await db.SaveChangesAsync(ct);

        // Snapshot po synchronizaci — aktualizuje graf
        if (wallet.SyncStatus == SyncStatus.Ok)
        {
            try { await portfolio.TakeSnapshotAsync(wallet.AccountId, ct); }
            catch { /* non-fatal */ }
        }
    }

    // ── Bitcoin dispatcher ───────────────────────────────────
    private async Task<bool> SyncBitcoinAsync(WatchedWallet wallet, CancellationToken ct)
    {
        string addr = wallet.Address.Trim();

        // xpub / ypub / zpub → HD peněženka → blockchain.info multiaddr
        // (result field = net BTC změna přes všechny odvozené adresy)
        if (addr.StartsWith("xpub", StringComparison.OrdinalIgnoreCase) ||
            addr.StartsWith("ypub", StringComparison.OrdinalIgnoreCase) ||
            addr.StartsWith("zpub", StringComparison.OrdinalIgnoreCase))
            return await SyncBitcoinXpubAsync(wallet, addr, ct);

        // Single address (1…, 3…, bc1q…) → blockstream.info
        // (scriptpubkey_address spolehlivě vrací bech32)
        return await SyncBitcoinAddressAsync(wallet, addr, ct);
    }

    // ── Bitcoin: single address via blockstream.info ─────────
    private async Task<bool> SyncBitcoinAddressAsync(WatchedWallet wallet, string addr, CancellationToken ct)
    {
        var    asset    = await EnsureAssetAsync(wallet.Account.BaseCurrency, ct);
        int    imported = 0;
        int    total    = 0;
        string? lastTxid = null;

        for (int page = 0; page < 20; page++)
        {
            string url = $"https://blockstream.info/api/address/{Uri.EscapeDataString(addr)}/txs";
            if (lastTxid is not null) url += $"?last_seen_txid={lastTxid}";

            string json;
            try
            {
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    logger.LogWarning("blockstream.info HTTP {Status} pro {Addr}", (int)resp.StatusCode, addr);
                    return false;
                }
                json = await resp.Content.ReadAsStringAsync(ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("blockstream.info nedostupný: {Msg}", ex.Message);
                return false;
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("blockstream neočekávaný formát pro {Addr}: {Json}",
                    addr, json.Length > 200 ? json[..200] : json);
                return false;
            }

            int pageCount = 0;
            foreach (var tx in doc.RootElement.EnumerateArray())
            {
                pageCount++; total++;
                string? txid = tx.TryGetProperty("txid", out var tidEl) ? tidEl.GetString() : null;
                if (txid is null) continue;
                lastTxid = txid;

                long blockTime = 0;
                if (tx.TryGetProperty("status", out var stEl) &&
                    stEl.TryGetProperty("block_time", out var btEl))
                    blockTime = btEl.GetInt64();
                if (blockTime == 0) continue; // nepotvrzená tx

                if (await db.Transactions.AnyAsync(
                    t => t.AccountId == wallet.AccountId && t.Note == txid, ct))
                    continue;

                long received = 0;
                if (tx.TryGetProperty("vout", out var voutsEl))
                    foreach (var o in voutsEl.EnumerateArray())
                        if (o.TryGetProperty("scriptpubkey_address", out var aEl) &&
                            string.Equals(aEl.GetString(), addr, StringComparison.OrdinalIgnoreCase) &&
                            o.TryGetProperty("value", out var vEl))
                            received += vEl.GetInt64();

                long sent = 0;
                if (tx.TryGetProperty("vin", out var vinsEl))
                    foreach (var inp in vinsEl.EnumerateArray())
                        if (inp.TryGetProperty("prevout", out var po) &&
                            po.TryGetProperty("scriptpubkey_address", out var aEl) &&
                            string.Equals(aEl.GetString(), addr, StringComparison.OrdinalIgnoreCase) &&
                            po.TryGetProperty("value", out var vEl))
                            sent += vEl.GetInt64();

                long net = received - sent;
                if (net == 0) continue;

                db.Transactions.Add(new EfTransaction
                {
                    Id           = Guid.NewGuid(),
                    AccountId    = wallet.AccountId,
                    AssetId      = asset.Id,
                    Date         = DateTimeOffset.FromUnixTimeSeconds(blockTime).UtcDateTime,
                    Type         = net > 0 ? TransactionType.Deposit : TransactionType.Withdrawal,
                    Quantity     = Math.Abs(net) / 100_000_000m,
                    PricePerUnit = 1m,
                    Note         = txid,
                    Category     = "Import",
                });
                imported++;
            }

            if (pageCount < 25) break;
            await Task.Delay(200, ct);
        }

        logger.LogInformation("BTC addr sync {Addr}: {Total} tx, {Imported} nových", addr, total, imported);

        if (imported > 0)
        {
            await db.SaveChangesAsync(ct);
            await RecalcHoldingAsync(wallet.AccountId, asset.Id, ct);
        }
        return true;
    }

    // ── Bitcoin: xpub/ypub/zpub — BIP32 derivace + blockstream.info ────────
    // xpub může být exportován z jakéhokoliv typu peněženky (BIP44/49/84).
    // Skenujeme všechny 3 script typy najednou — každý má vlastní gap counter.
    // zpub → jen Segwit, ypub → jen SegwitP2SH, xpub → všechny tři typy.
    private async Task<bool> SyncBitcoinXpubAsync(WatchedWallet wallet, string xpubRaw, CancellationToken ct)
    {
        ExtPubKey extKey;
        try { extKey = ParseXpub(xpubRaw); }
        catch (Exception ex)
        {
            logger.LogWarning("Nelze parsovat extended key: {Msg}", ex.Message);
            return false;
        }

        // zpub → jen Segwit (bc1q), ypub → jen P2SH (3...), xpub → všechny tři
        ScriptPubKeyType[] typesToScan = xpubRaw.StartsWith("zpub", StringComparison.OrdinalIgnoreCase)
            ? new[] { ScriptPubKeyType.Segwit }
            : xpubRaw.StartsWith("ypub", StringComparison.OrdinalIgnoreCase)
                ? new[] { ScriptPubKeyType.SegwitP2SH }
                : new[] { ScriptPubKeyType.Segwit, ScriptPubKeyType.SegwitP2SH, ScriptPubKeyType.Legacy };

        var asset    = await EnsureAssetAsync(wallet.Account.BaseCurrency, ct);
        int imported = 0;
        const int gapLimit = 20;

        // Prohledej external chain (0 = příchozí) i change chain (1 = drobné)
        for (int chain = 0; chain <= 1; chain++)
        {
            var chainKey = extKey.Derive((uint)chain);

            // Každý script typ má vlastní gap counter
            var gaps = new int[typesToScan.Length];

            for (int idx = 0; idx < 2000; idx++)
            {
                if (ct.IsCancellationRequested) break;
                if (gaps.All(g => g >= gapLimit)) break; // všechny typy dosáhly gap limitu

                var childKey = chainKey.Derive((uint)idx);

                for (int si = 0; si < typesToScan.Length; si++)
                {
                    if (gaps[si] >= gapLimit) continue;

                    string addr = childKey.PubKey
                        .GetAddress(typesToScan[si], NBitcoin.Network.Main).ToString();

                    string json;
                    try
                    {
                        using var resp = await _http.GetAsync(
                            $"https://blockstream.info/api/address/{addr}/txs", ct);
                        if (!resp.IsSuccessStatusCode) { gaps[si]++; continue; }
                        json = await resp.Content.ReadAsStringAsync(ct);
                    }
                    catch { gaps[si]++; continue; }

                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array ||
                        doc.RootElement.GetArrayLength() == 0)
                    {
                        gaps[si]++;
                        continue;
                    }

                    gaps[si] = 0; // adresa má transakce → reset gap

                    foreach (var tx in doc.RootElement.EnumerateArray())
                    {
                        string? txid = tx.TryGetProperty("txid", out var tidEl) ? tidEl.GetString() : null;
                        if (txid is null) continue;

                        long blockTime = 0;
                        if (tx.TryGetProperty("status", out var stEl) &&
                            stEl.TryGetProperty("block_time", out var btEl))
                            blockTime = btEl.GetInt64();
                        if (blockTime == 0) continue;

                        if (await db.Transactions.AnyAsync(
                            t => t.AccountId == wallet.AccountId && t.Note == txid, ct))
                            continue;

                        long received = 0;
                        if (tx.TryGetProperty("vout", out var voutsEl))
                            foreach (var o in voutsEl.EnumerateArray())
                                if (o.TryGetProperty("scriptpubkey_address", out var aEl) &&
                                    string.Equals(aEl.GetString(), addr, StringComparison.OrdinalIgnoreCase) &&
                                    o.TryGetProperty("value", out var vEl))
                                    received += vEl.GetInt64();

                        long sent = 0;
                        if (tx.TryGetProperty("vin", out var vinsEl))
                            foreach (var inp in vinsEl.EnumerateArray())
                                if (inp.TryGetProperty("prevout", out var po) &&
                                    po.TryGetProperty("scriptpubkey_address", out var aEl) &&
                                    string.Equals(aEl.GetString(), addr, StringComparison.OrdinalIgnoreCase) &&
                                    po.TryGetProperty("value", out var vEl))
                                    sent += vEl.GetInt64();

                        long net = received - sent;
                        if (net == 0) continue;

                        db.Transactions.Add(new EfTransaction
                        {
                            Id           = Guid.NewGuid(),
                            AccountId    = wallet.AccountId,
                            AssetId      = asset.Id,
                            Date         = DateTimeOffset.FromUnixTimeSeconds(blockTime).UtcDateTime,
                            Type         = net > 0 ? TransactionType.Deposit : TransactionType.Withdrawal,
                            Quantity     = Math.Abs(net) / 100_000_000m,
                            PricePerUnit = 1m,
                            Note         = txid,
                            Category     = "Import",
                        });
                        imported++;
                    }

                    await Task.Delay(80, ct); // rate limit
                }
            }
        }

        logger.LogInformation("BTC xpub sync: {Imported} nových transakcí (typy={Types})",
            imported, string.Join("+", typesToScan.Select(t => t.ToString())));

        if (imported > 0)
        {
            await db.SaveChangesAsync(ct);
            await RecalcHoldingAsync(wallet.AccountId, asset.Id, ct);
        }
        return true;
    }

    /// <summary>Parsuje xpub/ypub/zpub → ExtPubKey (normalizuje verzi zpub/ypub → xpub).</summary>
    private static ExtPubKey ParseXpub(string raw)
    {
        if (raw.StartsWith("zpub", StringComparison.OrdinalIgnoreCase))
            raw = RebaseVersionBytes(raw,
                      new byte[] { 0x04, 0xB2, 0x47, 0x46 },
                      new byte[] { 0x04, 0x88, 0xB2, 0x1E });
        else if (raw.StartsWith("ypub", StringComparison.OrdinalIgnoreCase))
            raw = RebaseVersionBytes(raw,
                      new byte[] { 0x04, 0x9D, 0x7C, 0xB2 },
                      new byte[] { 0x04, 0x88, 0xB2, 0x1E });
        // xpub: žádná konverze, parsujeme přímo
        return ExtPubKey.Parse(raw, NBitcoin.Network.Main);
    }

    /// <summary>Přepíše první 4 verzi-bajty base58check klíče (zpub/ypub → xpub).</summary>
    private static string RebaseVersionBytes(string key, byte[] from, byte[] to)
    {
        byte[] data = Encoders.Base58Check.DecodeData(key);
        if (data.Length < 4) throw new ArgumentException("Příliš krátký klíč");
        Array.Copy(to, 0, data, 0, 4);
        return Encoders.Base58Check.EncodeData(data);
    }

    // ── Cosmos / ATOM (cosmos.directory LCD relay) ───────────
    // cosmos.directory poskytuje volný relay pro ~100 IBC řetězců.
    // Dotazujeme se na transakce kde je adresa příjemce nebo odesílatele.
    // TX events format: SDK < 0.47 → logs[].events[], SDK 0.47+ → events[] (top level).
    private async Task<bool> SyncCosmosAsync(WatchedWallet wallet, CancellationToken ct)
    {
        string addr    = wallet.Address.Trim();
        string baseUrl = "https://rest.cosmos.directory/cosmoshub";
        var    asset   = await EnsureAssetAsync(wallet.Account.BaseCurrency, ct);
        int    imported = 0;

        // Hledáme txs kde je adresa příjemce i txs kde je odesílatelem
        var eventFilters = new[]
        {
            $"transfer.recipient={addr}",
            $"transfer.sender={addr}",
        };

        foreach (string evtFilter in eventFilters)
        {
            string json;
            try
            {
                string url = $"{baseUrl}/cosmos/tx/v1beta1/txs" +
                             $"?events={Uri.EscapeDataString(evtFilter)}" +
                             $"&pagination.limit=100&order_by=2"; // 2 = ORDER_BY_DESC
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    logger.LogWarning("Cosmos LCD HTTP {S} pro {Addr}", (int)resp.StatusCode, addr);
                    continue;
                }
                json = await resp.Content.ReadAsStringAsync(ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Cosmos LCD nedostupný: {Msg}", ex.Message);
                continue;
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tx_responses", out var responses)) continue;

            foreach (var txResp in responses.EnumerateArray())
            {
                string? txhash = txResp.TryGetProperty("txhash", out var hEl) ? hEl.GetString() : null;
                string? tsStr  = txResp.TryGetProperty("timestamp", out var tsEl) ? tsEl.GetString() : null;
                if (txhash is null || tsStr is null) continue;

                if (!DateTime.TryParse(tsStr, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var date))
                    continue;
                date = DateTime.SpecifyKind(date, DateTimeKind.Utc);

                if (await db.Transactions.AnyAsync(
                    t => t.AccountId == wallet.AccountId && t.Note == txhash, ct))
                    continue;

                long receivedUatom = 0;
                long sentUatom     = 0;

                // SDK < 0.47 → čti z logs[].events[]
                if (txResp.TryGetProperty("logs", out var logsEl))
                    foreach (var log in logsEl.EnumerateArray())
                        if (log.TryGetProperty("events", out var evts))
                            ExtractCosmosTransfer(evts, addr, ref receivedUatom, ref sentUatom);

                // SDK 0.47+ → čti z events[] (top level)
                if (txResp.TryGetProperty("events", out var topEvts))
                    ExtractCosmosTransfer(topEvts, addr, ref receivedUatom, ref sentUatom);

                long net = receivedUatom - sentUatom;
                if (net == 0) continue;

                decimal atomQty = Math.Abs(net) / 1_000_000m;
                if (atomQty < 0.000001m) continue;

                db.Transactions.Add(new EfTransaction
                {
                    Id           = Guid.NewGuid(),
                    AccountId    = wallet.AccountId,
                    AssetId      = asset.Id,
                    Date         = date,
                    Type         = net > 0 ? TransactionType.Deposit : TransactionType.Withdrawal,
                    Quantity     = atomQty,
                    PricePerUnit = 1m,
                    Note         = txhash,
                    Category     = "Import",
                });
                imported++;
            }

            await Task.Delay(300, ct); // rate limit mezi dvěma dotazy
        }

        logger.LogInformation("ATOM sync {Addr}: {Imported} nových transakcí", addr, imported);

        if (imported > 0)
        {
            await db.SaveChangesAsync(ct);
            await RecalcHoldingAsync(wallet.AccountId, asset.Id, ct);
        }
        return true;
    }

    /// <summary>Projde pole events a přičte uatom příjemce/odesílatele dané adresy.</summary>
    private static void ExtractCosmosTransfer(
        JsonElement eventsEl, string addr,
        ref long receivedUatom, ref long sentUatom)
    {
        foreach (var evt in eventsEl.EnumerateArray())
        {
            // Typ eventu může být "transfer" nebo base64-kódovaný v SDK 0.47+
            bool isTransfer = false;
            if (evt.TryGetProperty("type", out var typeEl))
            {
                string? t = typeEl.GetString();
                isTransfer = t == "transfer" ||
                             t == "dHJhbnNmZXI="; // base64("transfer") v některých SDK verzích
            }
            if (!isTransfer) continue;
            if (!evt.TryGetProperty("attributes", out var attrsEl)) continue;

            // Attributy mohou mít klíče jako plain text nebo base64
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var attr in attrsEl.EnumerateArray())
            {
                string? k = attr.TryGetProperty("key",   out var kEl) ? kEl.GetString() : null;
                string? v = attr.TryGetProperty("value", out var vEl) ? vEl.GetString() : null;
                if (k is null || v is null) continue;
                // Decode base64 keys if needed (SDK 0.47+ ABCI event encoding)
                try { k = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(k)); } catch { }
                try { v = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(v)); } catch { }
                attrs[k] = v;
            }

            long uatom = ParseUatom(attrs.GetValueOrDefault("amount"));
            if (attrs.TryGetValue("recipient", out string? rec) &&
                string.Equals(rec, addr, StringComparison.OrdinalIgnoreCase))
                receivedUatom += uatom;
            if (attrs.TryGetValue("sender", out string? snd) &&
                string.Equals(snd, addr, StringComparison.OrdinalIgnoreCase))
                sentUatom += uatom;
        }
    }

    /// <summary>Parsuje "1000000uatom" nebo "1000000uatom,500ibc/..." → počet uatom.</summary>
    private static long ParseUatom(string? amountStr)
    {
        if (string.IsNullOrEmpty(amountStr)) return 0;
        foreach (var part in amountStr.Split(','))
        {
            int idx = part.IndexOf("uatom", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            if (long.TryParse(part[..idx], out long val)) return val;
        }
        return 0;
    }

    // ── Ethereum (Etherscan public) ──────────────────────────
    private async Task<bool> SyncEthereumAsync(WatchedWallet wallet, CancellationToken ct)
    {
        string addr = wallet.Address.ToLowerInvariant();
        string url  = $"https://api.etherscan.io/api?module=account&action=txlist" +
                      $"&address={addr}&startblock=0&endblock=99999999&sort=asc&page=1&offset=100";
        string json;
        try { json = await _http.GetStringAsync(url, ct); }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Etherscan nedostupný: {Msg}", ex.Message);
            return false;
        }

        using var doc = JsonDocument.Parse(json);

        // Etherscan vrací {"status":"0","message":"NOTOK","result":"..."} při chybě/rate limit
        if (doc.RootElement.TryGetProperty("status", out var stEl) && stEl.GetString() == "0")
        {
            string msg = doc.RootElement.TryGetProperty("message", out var mEl)
                ? (mEl.GetString() ?? "NOTOK") : "NOTOK";
            logger.LogWarning("Etherscan API chyba pro {Addr}: {Msg}", addr, msg);
            return false;
        }

        if (!doc.RootElement.TryGetProperty("result", out var resultEl) ||
            resultEl.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning("Etherscan neočekávaný formát pro {Addr}", addr);
            return false;
        }

        var asset    = await EnsureAssetAsync(wallet.Account.BaseCurrency, ct);
        int imported = 0;

        foreach (var tx in resultEl.EnumerateArray())
        {
            // Přeskočit selhané transakce
            if (tx.TryGetProperty("isError", out var errEl) && errEl.GetString() == "1") continue;

            string? hash     = tx.TryGetProperty("hash",      out var hEl) ? hEl.GetString()  : null;
            string? fromStr  = tx.TryGetProperty("from",      out var fEl) ? fEl.GetString()  : null;
            string? toStr    = tx.TryGetProperty("to",        out var tEl) ? tEl.GetString()  : null;
            string? valStr   = tx.TryGetProperty("value",     out var vEl) ? vEl.GetString()  : null;
            string? tsStr    = tx.TryGetProperty("timeStamp", out var tsEl)? tsEl.GetString() : null;
            if (hash is null || valStr is null || tsStr is null) continue;

            if (!decimal.TryParse(valStr, out decimal weiVal) || weiVal == 0) continue;
            if (!long.TryParse(tsStr, out long unix)) continue;

            decimal ethQty = weiVal / 1_000_000_000_000_000_000m;
            if (ethQty < 0.000001m) continue;

            bool isDeposit    = string.Equals(toStr,   addr, StringComparison.OrdinalIgnoreCase);
            bool isWithdrawal = string.Equals(fromStr, addr, StringComparison.OrdinalIgnoreCase);
            if (!isDeposit && !isWithdrawal) continue;

            if (await db.Transactions.AnyAsync(t => t.AccountId == wallet.AccountId && t.Note == hash, ct))
                continue;

            var type = isDeposit ? TransactionType.Deposit : TransactionType.Withdrawal;
            var date = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;

            db.Transactions.Add(new EfTransaction
            {
                Id           = Guid.NewGuid(),
                AccountId    = wallet.AccountId,
                AssetId      = asset.Id,
                Date         = date,
                Type         = type,
                Quantity     = ethQty,
                PricePerUnit = 1m,
                Note         = hash,
                Category     = "Import",
            });
            imported++;
        }

        logger.LogInformation("ETH sync {Addr}: {Imported} nových transakcí importováno", addr, imported);

        if (imported > 0)
        {
            await db.SaveChangesAsync(ct);
            await RecalcHoldingAsync(wallet.AccountId, asset.Id, ct);
        }

        return true;
    }

    // ── BlockCypher (Litecoin + fallback) ────────────────────
    private async Task<bool> SyncBlockCypherAsync(WatchedWallet wallet, string coin, CancellationToken ct)
    {
        string url = $"https://api.blockcypher.com/v1/{coin}/main/addrs/{wallet.Address}/full?limit=50";
        string json;
        try { json = await _http.GetStringAsync(url, ct); }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("BlockCypher nedostupný: {Msg}", ex.Message);
            return false;
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("txs", out var txsEl)) return false;

        var asset    = await EnsureAssetAsync(wallet.Account.BaseCurrency, ct);
        int imported = 0;

        foreach (var tx in txsEl.EnumerateArray())
        {
            string? hash    = tx.TryGetProperty("hash",     out var hEl) ? hEl.GetString() : null;
            string? recvStr = tx.TryGetProperty("received", out var rEl) ? rEl.GetString() : null;
            if (hash is null || recvStr is null) continue;

            if (!DateTime.TryParse(recvStr, out var date)) continue;
            date = DateTime.SpecifyKind(date, DateTimeKind.Utc);

            if (await db.Transactions.AnyAsync(t => t.AccountId == wallet.AccountId && t.Note == hash, ct))
                continue;

            // BlockCypher outputs: addr + value (satoshi)
            long received = 0, sent = 0;
            if (tx.TryGetProperty("outputs", out var outs))
                foreach (var o in outs.EnumerateArray())
                    if (o.TryGetProperty("addresses", out var addrs))
                        foreach (var a in addrs.EnumerateArray())
                            if (a.GetString() == wallet.Address &&
                                o.TryGetProperty("value", out var vEl))
                            { received += vEl.GetInt64(); break; }

            if (tx.TryGetProperty("inputs", out var ins))
                foreach (var inp in ins.EnumerateArray())
                    if (inp.TryGetProperty("addresses", out var addrs))
                        foreach (var a in addrs.EnumerateArray())
                            if (a.GetString() == wallet.Address &&
                                inp.TryGetProperty("output_value", out var vEl))
                            { sent += vEl.GetInt64(); break; }

            long net = received - sent;
            if (net == 0) continue;

            decimal qty  = Math.Abs(net) / 100_000_000m;
            var     type = net > 0 ? TransactionType.Deposit : TransactionType.Withdrawal;

            db.Transactions.Add(new EfTransaction
            {
                Id = Guid.NewGuid(), AccountId = wallet.AccountId, AssetId = asset.Id,
                Date = date, Type = type, Quantity = qty, PricePerUnit = 1m,
                Note = hash, Category = "Import",
            });
            imported++;
        }

        if (imported > 0)
        {
            await db.SaveChangesAsync(ct);
            await RecalcHoldingAsync(wallet.AccountId, asset.Id, ct);
        }

        return true;
    }

    // ── Helpers ──────────────────────────────────────────────

    /// <summary>Přepočítá holding ze všech transakcí — nahrazuje manuální +=.</summary>
    private async Task RecalcHoldingAsync(Guid accountId, Guid assetId, CancellationToken ct)
    {
        var txs = await db.Transactions
            .Where(t => t.AccountId == accountId && t.AssetId == assetId)
            .ToListAsync(ct);

        decimal totalQty = txs.Sum(t =>
            t.Type == TransactionType.Deposit ? t.Quantity : -t.Quantity);

        var holding = await db.Holdings.FirstOrDefaultAsync(
            h => h.AccountId == accountId && h.AssetId == assetId, ct);

        if (holding is null)
        {
            db.Holdings.Add(new Holding
            {
                Id = Guid.NewGuid(), AccountId = accountId, AssetId = assetId,
                Quantity = Math.Max(0m, totalQty), CostBasis = 0m,
            });
        }
        else
        {
            holding.Quantity = Math.Max(0m, totalQty);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<Asset> EnsureAssetAsync(string symbol, CancellationToken ct)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Symbol == symbol, ct);
        if (asset is not null) return asset;

        asset = new Asset
        {
            Id = Guid.NewGuid(), Symbol = symbol, Name = symbol,
            AssetType = AssetType.Crypto,
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync(ct);
        return asset;
    }
}
