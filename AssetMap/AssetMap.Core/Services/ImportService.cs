using AssetMap.Database;
using AssetMap.Entities;
using AssetMap.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace AssetMap.Core.Services;

/// <summary>
/// Importuje transakce z CSV do zadaného účtu.
///
/// Podporované formáty (automatická detekce):
///   • generic    — Date,Type,Amount[,Note]
///   • revolut    — Revolut statement export (CZK/EUR)
///   • trading212 — Trading212 activity statement
/// </summary>
public class ImportService(AppDbContext db, IPortfolioService portfolio) : IImportService
{
    public async Task<ImportResult> ImportCsvAsync(
        Guid   accountId,
        Guid   userId,
        string fileName,
        Stream csvStream,
        CancellationToken ct = default)
    {
        // Načti bytes — potřebujeme detekovat encoding
        using var ms = new MemoryStream();
        await csvStream.CopyToAsync(ms, ct);
        byte[] rawBytes = ms.ToArray();

        // Dekóduj: zkus UTF-8, fallback na Windows-1250 (české banky)
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        string rawUtf8 = System.Text.Encoding.UTF8.GetString(rawBytes);
        string raw = rawUtf8.Contains('�')
            ? System.Text.Encoding.GetEncoding(1250).GetString(rawBytes)
            : rawUtf8;

        var lines = raw.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
            return new ImportResult { DetectedFormat = "unknown", TotalRows = 0 };

        // Zjisti formát ze všech řádků (KB má metadata před hlavičkou)
        string format = DetectFormat(lines);

        // Najdi první datový řádek (pro KB přeskoč metadata)
        int dataStart = 1;
        if (format == "kb")
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("Datum zauctovani", StringComparison.OrdinalIgnoreCase))
                {
                    dataStart = i + 1;
                    break;
                }
            }
        }

        // Načti účet
        var account = await db.Accounts
            .Include(a => a.Holdings).ThenInclude(h => h.Asset)
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
            throw new InvalidOperationException($"Account {accountId} not found.");

        // Najdi nebo vytvoř asset
        var asset = await db.Assets.FirstOrDefaultAsync(
            a => a.Symbol == account.BaseCurrency, ct);
        if (asset is null)
        {
            asset = new Asset
            {
                Id        = Guid.NewGuid(),
                Symbol    = account.BaseCurrency,
                Name      = account.BaseCurrency,
                AssetType = account.AccountType == AccountType.CryptoWallet
                            ? AssetType.Crypto : AssetType.Fiat,
            };
            db.Assets.Add(asset);
        }

        // Vytvoř ImportBatch
        var batch = new ImportBatch
        {
            Id         = Guid.NewGuid(),
            UserId     = userId,
            AccountId  = accountId,
            FileName   = fileName,
            ImportedAt = DateTime.UtcNow,
            Status     = ImportBatchStatus.Pending,
        };
        db.ImportBatches.Add(batch);

        // Parsuj řádky
        var result = new ImportResult
        {
            ImportBatchId  = batch.Id,
            DetectedFormat = format switch
            {
                "kb"         => "Komerční banka",
                "revolut"    => "Revolut",
                "trading212" => "Trading 212",
                _            => "Generic",
            },
        };

        var txsToAdd     = new List<Transaction>();
        decimal holdingDelta = 0m;

        for (int i = dataStart; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            result.TotalRows++;

            try
            {
                var parsed = format switch
                {
                    "revolut"    => ParseRevolutRow(line),
                    "trading212" => ParseTrading212Row(line),
                    "kb"         => ParseKbRow(line, account.BaseCurrency),
                    _            => ParseGenericRow(line),
                };

                if (parsed is null) continue;   // prázdný / přeskočený řádek

                var tx = new Transaction
                {
                    Id            = Guid.NewGuid(),
                    AccountId     = accountId,
                    Date          = parsed.Date,
                    Type          = parsed.Type,
                    AssetId       = asset.Id,
                    Asset         = asset,
                    Quantity      = parsed.Amount,
                    PricePerUnit  = 1m,
                    Note          = parsed.Note,
                    ImportBatchId = batch.Id,
                };
                txsToAdd.Add(tx);

                holdingDelta += parsed.Type == TransactionType.Deposit
                    ? parsed.Amount : -parsed.Amount;

                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.ErrorCount++;
                result.Errors.Add(new ImportRowError(i + 1, ex.Message));
            }
        }

        // Počáteční zůstatek z hlavičky (KB) — použijeme pro holding i snapshoty
        decimal initialBalance = format == "kb" ? ParseKbInitialBalance(lines) : 0m;

        // Ulož všechny transakce najednou
        db.Transactions.AddRange(txsToAdd);

        // Aktualizuj holding
        var holding = account.Holdings.FirstOrDefault(h => h.AssetId == asset.Id);
        if (holding is null)
        {
            holding = new Holding
            {
                Id        = Guid.NewGuid(),
                AccountId = accountId,
                AssetId   = asset.Id,
                Quantity  = 0m,
                CostBasis = 0m,
            };
            db.Holdings.Add(holding);
        }
        // Pokud je účet čerstvý (0) a CSV obsahuje počáteční zůstatek, použij ho jako základ
        decimal baseQty = (holding.Quantity == 0m && initialBalance > 0m)
            ? initialBalance
            : holding.Quantity;
        holding.Quantity = Math.Max(0m, baseQty + holdingDelta);

        // Finalizuj batch
        batch.RowCount     = result.TotalRows;
        batch.SuccessCount = result.SuccessCount;
        batch.Status       = result.ErrorCount == 0
            ? ImportBatchStatus.Done
            : ImportBatchStatus.PartialError;
        batch.Notes = result.ErrorCount > 0
            ? $"{result.ErrorCount} řádků s chybou"
            : null;

        await db.SaveChangesAsync(ct);

        // Retroaktivně oprav PortfolioSnapshots z transakcí
        await RebuildSnapshotsFromTransactionsAsync(
            accountId, account.UserId, asset.Id, txsToAdd, initialBalance, ct);

        // Snapshot pro dnešek (aktuální stav)
        await portfolio.TakeSnapshotAsync(accountId, ct);

        return result;
    }

    // ── Retroaktivní PortfolioSnapshots ────────────────────────────
    private async Task RebuildSnapshotsFromTransactionsAsync(
        Guid accountId, Guid userId, Guid assetId,
        List<Transaction> txs, decimal initialBalance,
        CancellationToken ct)
    {
        if (txs.Count == 0) return;

        // Aktuální cena assetu v USD
        var latestPrice = await db.PriceSnapshots
            .Where(p => p.AssetId == assetId)
            .OrderByDescending(p => p.Timestamp)
            .Select(p => p.Price)
            .FirstOrDefaultAsync(ct);
        double priceUsd = latestPrice > 0 ? (double)latestPrice : 1.0;

        // Kumulativní zůstatek per den (počínaje initialBalance)
        var byDate = txs
            .OrderBy(t => t.Date)
            .GroupBy(t => t.Date.Date)
            .ToList();

        decimal running = initialBalance;
        var dailies = new List<(DateTime Day, decimal Balance)>();

        foreach (var grp in byDate)
        {
            foreach (var tx in grp.OrderBy(t => t.Date))
                running += tx.Type == TransactionType.Deposit ? tx.Quantity : -tx.Quantity;
            if (running < 0) running = 0m;
            dailies.Add((grp.Key, running));
        }

        // Smaž staré snapshoty pro tyto dny (většinou 0 z doby před importem)
        var datesToFix = dailies.Select(d => d.Day).ToList();
        var old = await db.PortfolioSnapshots
            .Where(s => s.AccountId == accountId && datesToFix.Contains(s.Timestamp.Date))
            .ToListAsync(ct);
        db.PortfolioSnapshots.RemoveRange(old);

        // Vytvoř nové snapshoty ve 23:00 UTC každého dne
        foreach (var (day, balance) in dailies)
        {
            db.PortfolioSnapshots.Add(new PortfolioSnapshot
            {
                Id         = Guid.NewGuid(),
                UserId     = userId,
                AccountId  = accountId,
                TotalValue = balance * (decimal)priceUsd,
                Timestamp  = DateTime.SpecifyKind(day.AddHours(23), DateTimeKind.Utc),
            });
        }

        await db.SaveChangesAsync(ct);
    }

    // ── KB: počáteční zůstatek z hlavičky ──────────────────────────
    // Řádek: "Pocatecni zustatek;40470,07;;;;;..."
    private static decimal ParseKbInitialBalance(string[] lines)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith("Pocatecni zustatek;", StringComparison.OrdinalIgnoreCase))
            {
                var cols = line.Split(';');
                if (cols.Length > 1 && !string.IsNullOrWhiteSpace(cols[1]))
                    try { return ParseDecimal(cols[1]); } catch { }
            }
        }
        return 0m;
    }

    // ── Detekce formátu ─────────────────────────────────────────────
    private static string DetectFormat(string[] lines)
    {
        // KB: první řádek začíná "KB+"
        if (lines[0].TrimStart().StartsWith("KB+", StringComparison.OrdinalIgnoreCase))
            return "kb";

        var h = lines[0].ToLowerInvariant();
        if (h.Contains("started date") && h.Contains("product")) return "revolut";
        if (h.Contains("isin")         && h.Contains("ticker"))  return "trading212";
        return "generic";
    }

    // ── Parsovaná řádka ─────────────────────────────────────────────
    private record ParsedRow(DateTime Date, TransactionType Type, decimal Amount, string? Note);

    // ── Generic: Date,Type,Amount[,Note] ────────────────────────────
    // Příklad: 2024-01-15,Deposit,5000,Výplata
    private static ParsedRow? ParseGenericRow(string line)
    {
        var cols = SplitCsv(line);
        if (cols.Length < 3) return null;

        DateTime date   = ParseDate(cols[0].Trim());
        var      type   = ParseType(cols[1].Trim());
        decimal  amount = ParseDecimal(cols[2].Trim());
        string?  note   = cols.Length > 3 ? cols[3].Trim().Trim('"') : null;

        return new ParsedRow(date, type, Math.Abs(amount), note);
    }

    // ── Revolut ─────────────────────────────────────────────────────
    // Sloupce: Type,Product,Started Date,Completed Date,Description,Amount,Fee,Currency,State,Balance
    private static ParsedRow? ParseRevolutRow(string line)
    {
        var cols = SplitCsv(line);
        if (cols.Length < 9) return null;

        string txType   = cols[0].Trim().ToUpperInvariant();
        string state    = cols[8].Trim().ToUpperInvariant();

        // Přeskočit nedokončené transakce
        if (state is not ("COMPLETED" or "REVERTED")) return null;

        DateTime date   = ParseDate(cols[2].Trim());   // Started Date
        string   desc   = cols[4].Trim().Trim('"');
        decimal  amount = ParseDecimal(cols[5].Trim());

        // Revolut typy → Deposit / Withdrawal
        var type = txType switch
        {
            "TOPUP" or "TRANSFER" when amount > 0 => TransactionType.Deposit,
            "TRANSFER" or "CARD_PAYMENT"           => TransactionType.Withdrawal,
            _ when amount >= 0                     => TransactionType.Deposit,
            _                                      => TransactionType.Withdrawal,
        };

        return new ParsedRow(date, type, Math.Abs(amount), desc);
    }

    // ── KB (Komerční banka) ─────────────────────────────────────────
    // Sloupce (;-separated): DatumZauctovani;DatumProvedeni;Protistrana;NazevProtiuctu;
    //                         Castka;Mena;OrigCastka;OrigMena;Kurz;VS;KS;SS;
    //                         IdentTransakce;TypTransakce;PopisProMne;ZpravaProPrijemce;...
    // Záporná Castka = výběr, kladná = příjem. Desetinný oddělovač = čárka.
    private static ParsedRow? ParseKbRow(string line, string? accountCurrency = null)
    {
        var cols = SplitCsv(line, ';');
        if (cols.Length < 5) return null;

        // Col 1 = Datum provedeni
        string dateStr = cols[1].Trim();
        if (string.IsNullOrWhiteSpace(dateStr)) return null;
        DateTime date = ParseDate(dateStr);

        // Col 5 = Mena — přeskoč řádky s jinou měnou než účet
        string rowCurrency = cols.Length > 5 ? cols[5].Trim() : "";
        if (!string.IsNullOrEmpty(accountCurrency) && !string.IsNullOrEmpty(rowCurrency) &&
            !string.Equals(rowCurrency, accountCurrency, StringComparison.OrdinalIgnoreCase))
            return null;

        // Col 4 = Castka
        string amtStr = cols[4].Trim();
        if (string.IsNullOrWhiteSpace(amtStr)) return null;
        decimal amount = ParseDecimal(amtStr);

        var type = amount >= 0 ? TransactionType.Deposit : TransactionType.Withdrawal;

        // Note: typ transakce — protistrana — zpráva příjemci
        string name    = cols.Length > 3  ? cols[3].Trim()  : "";
        string txType  = cols.Length > 13 ? cols[13].Trim() : "";
        string message = cols.Length > 15 ? cols[15].Trim() : "";

        var noteParts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(txType))  noteParts.Add(txType);
        if (!string.IsNullOrWhiteSpace(name))    noteParts.Add(name);
        if (!string.IsNullOrWhiteSpace(message)) noteParts.Add(message);
        string? note = noteParts.Count > 0 ? string.Join(" — ", noteParts) : null;

        return new ParsedRow(date, type, Math.Abs(amount), note);
    }

    // ── Trading212 ───────────────────────────────────────────────────
    // Sloupce: Action,Time,ISIN,Ticker,Name,No. of shares,Price / share,...,Total,...
    private static ParsedRow? ParseTrading212Row(string line)
    {
        var cols = SplitCsv(line);
        if (cols.Length < 9) return null;

        string action = cols[0].Trim().ToLowerInvariant();
        if (!action.Contains("buy") && !action.Contains("sell") &&
            !action.Contains("deposit") && !action.Contains("withdrawal"))
            return null;

        DateTime date = ParseDate(cols[1].Trim());

        // Celková hodnota je obvykle v sloupci 9 (indexováno od 0)
        decimal total = ParseDecimal(cols.Length > 9 ? cols[9].Trim() : cols[^1].Trim());

        var type = (action.Contains("buy") || action.Contains("deposit"))
            ? TransactionType.Deposit
            : TransactionType.Withdrawal;

        string? note = cols.Length > 4 ? cols[4].Trim().Trim('"') : null;

        return new ParsedRow(date, type, Math.Abs(total), note);
    }

    // ── CSV helpers ────────────────────────────────────────────────
    private static string[] SplitCsv(string line, char sep = ',')
    {
        // Split respektující uvozovky
        var  result  = new List<string>();
        bool inQuote = false;
        var  current = new System.Text.StringBuilder();

        foreach (char c in line)
        {
            if (c == '"') { inQuote = !inQuote; continue; }
            if (c == sep && !inQuote)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return [.. result];
    }

    private static DateTime ParseDate(string s)
    {
        // Zkus různé formáty
        string[] formats =
        [
            "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy",
            "yyyy-MM-dd HH:mm:ss", "MM/dd/yyyy", "dd-MM-yyyy",
            "d-M-yyyy",
        ];
        foreach (var fmt in formats)
            if (DateTime.TryParseExact(s, fmt,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        // Fallback: general parse
        if (DateTime.TryParse(s,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var result))
            return DateTime.SpecifyKind(result, DateTimeKind.Utc);

        throw new FormatException($"Nepodařilo se parsovat datum: '{s}'");
    }

    private static decimal ParseDecimal(string s)
    {
        // Odstraň mezery a znaky měny, nahraď čárku tečkou
        s = s.Replace(" ", "").Replace(" ", "")
             .Replace("CZK", "").Replace("EUR", "").Replace("USD", "")
             .Replace(",", ".");

        // Pokud je více teček (formát 1.234.567,89) — odstraň tisícový oddělovač
        int lastDot = s.LastIndexOf('.');
        if (lastDot >= 0)
        {
            string intPart = s[..lastDot].Replace(".", "");
            string decPart = s[(lastDot + 1)..];
            s = intPart + "." + decPart;
        }

        if (decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal d))
            return d;

        throw new FormatException($"Nepodařilo se parsovat číslo: '{s}'");
    }

    private static TransactionType ParseType(string s) =>
        s.ToLowerInvariant() switch
        {
            "deposit" or "příjem" or "vklad" or "d" or "příchozí" or "in"  => TransactionType.Deposit,
            "withdrawal" or "výběr" or "výdaj" or "w" or "odchozí" or "out" => TransactionType.Withdrawal,
            _ => throw new FormatException($"Neznámý typ transakce: '{s}'"),
        };
}
