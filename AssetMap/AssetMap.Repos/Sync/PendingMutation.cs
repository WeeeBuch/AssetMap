using System;

namespace AssetMap.Repos.Sync;

public enum MutationType
{
    CreateAccount,
    UpdateAccount,
    DeleteAccount,
    CreateTransaction,
}

/// <summary>
/// Jedna neuložená operace čekající na odeslání na server.
/// Ukládá se jako JSON do pending.json.
/// </summary>
public class PendingMutation
{
    public Guid        Id         { get; set; } = Guid.NewGuid();
    public DateTime    CreatedAt  { get; set; } = DateTime.UtcNow;
    public MutationType Type      { get; set; }

    /// <summary>HTTP metoda: POST / PUT / DELETE</summary>
    public string HttpMethod { get; set; } = "POST";

    /// <summary>Relativní cesta, např. /api/accounts nebo /api/accounts/{id}</summary>
    public string Endpoint   { get; set; } = "";

    /// <summary>Serializované tělo požadavku (prázdné pro DELETE).</summary>
    public string Payload    { get; set; } = "";

    /// <summary>Kolikrát byl pokus o odeslání (pro logování / debugging).</summary>
    public int    RetryCount { get; set; }

    /// <summary>
    /// Lokální GUID přiřazený offline (jen u CreateAccount).
    /// Po úspěšném sync se server GUID načte přes RefreshAsync.
    /// </summary>
    public Guid? LocalAccountId { get; set; }
}
