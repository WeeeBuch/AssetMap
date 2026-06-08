using AssetMap.Core.Models;
using AssetMap.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetMap.API.Controllers;

[ApiController]
[Route("api/accounts")]
public class AccountsController(IAccountService accounts) : ControllerBase
{
    // Singleton userId pro single-user self-hosted setup
    private static readonly Guid DefaultUserId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>Všechny účty včetně celé historie.</summary>
    [HttpGet("full")]
    public async Task<IActionResult> GetFull(CancellationToken ct) =>
        Ok(await accounts.GetAllAsync(DefaultUserId, ct));

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

        var dto = await accounts.CreateAsync(DefaultUserId, req, ct);
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
}
