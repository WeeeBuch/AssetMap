using AssetMap.Entities.Enums;

namespace AssetMap.Core.Models;

// ── Response ─────────────────────────────────────────────────────

/// <summary>
/// Celá data účtu posílaná klientovi.
/// BalanceHistoryUsd: 365 denních hodnot v USD (nejstarší první).
/// </summary>
public class AccountFullDto
{
    public Guid        Id              { get; set; }
    public string      Name            { get; set; } = "";
    public AccountType AccountType     { get; set; }
    public string?     Institution     { get; set; }
    public string?     IconColorHex    { get; set; }
    public string      BaseCurrency    { get; set; } = "";
    public double      CurrentBalance  { get; set; }   // native units (CZK, BTC…)
    public double      CurrentValueUsd { get; set; }   // aktuální hodnota v USD
    public double[]    BalanceHistoryUsd { get; set; } = []; // 365 USD hodnot
    public List<TransactionDto> RecentTransactions { get; set; } = [];
}

public class TransactionDto
{
    public Guid            Id            { get; set; }
    public DateTime        Date          { get; set; }
    public TransactionType Type          { get; set; }
    public string          AssetSymbol   { get; set; } = "";
    public decimal         Quantity      { get; set; }
    public decimal         PricePerUnit  { get; set; }
    public string?         Note          { get; set; }
    public Guid?           FromAccountId { get; set; }
    public Guid?           ToAccountId   { get; set; }
    public decimal?        Fee           { get; set; }
    public string?         Category      { get; set; }
}

// ── Requests ─────────────────────────────────────────────────────

public class CreateAccountRequest
{
    public string      Name          { get; set; } = "";
    public string      Institution   { get; set; } = "";
    public AccountType AccountType   { get; set; }
    public string      AssetSymbol   { get; set; } = "";
    public double      StartBalance  { get; set; }
    public double      UsdPrice      { get; set; }   // cena 1 native jednotky v USD
    public string?     IconColorHex  { get; set; }
    /// <summary>Adresa krypto peněženky (volitelná). Vytvoří WatchedWallet záznam.</summary>
    public string?     WalletAddress { get; set; }
    /// <summary>Blockchain síť (volitelná, jen pokud WalletAddress != null).</summary>
    public int?        WalletNetwork { get; set; }
}

public class UpdateAccountRequest
{
    public string      Name         { get; set; } = "";
    public string      Institution  { get; set; } = "";
    public AccountType AccountType  { get; set; }
    public string?     IconColorHex { get; set; }
}

public class CreateTransactionRequest
{
    public Guid            AccountId    { get; set; }
    public TransactionType Type         { get; set; }
    public double          Amount       { get; set; }
    public double          Fee          { get; set; }
    public DateTime        Date         { get; set; }
    public string?         Note         { get; set; }
    /// <summary>Pro Transfer: ID cílového účtu.</summary>
    public Guid?           ToAccountId  { get; set; }
    /// <summary>Pro Transfer: ID zdrojového účtu (null = aktuální accountId).</summary>
    public Guid?           FromAccountId { get; set; }
    public string?         Category      { get; set; }
}
