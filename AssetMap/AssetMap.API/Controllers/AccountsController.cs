using AssetMap.Core.Models;
using AssetMap.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetMap.API.Controllers;

[ApiController]
[Route("api/accounts")]
public class AccountsController(IAccountService accounts, IImportService importer) : ControllerBase
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
}
