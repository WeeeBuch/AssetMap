using AssetMap.Core.Models;
using AssetMap.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetMap.API.Controllers;

[ApiController]
[Route("api/transactions")]
public class TransactionsController(ITransactionService txService) : ControllerBase
{
    /// <summary>Transakce pro daný účet.</summary>
    [HttpGet]
    public async Task<IActionResult> GetForAccount(
        [FromQuery] Guid accountId,
        [FromQuery] int  limit = 50,
        CancellationToken ct = default)
    {
        if (accountId == Guid.Empty) return BadRequest("accountId is required.");
        return Ok(await txService.GetForAccountAsync(accountId, limit, ct));
    }

    /// <summary>Přidá transakci (příchozí / odchozí).</summary>
    [HttpPost]
    public async Task<IActionResult> Add(
        [FromBody] CreateTransactionRequest req, CancellationToken ct)
    {
        if (req.AccountId == Guid.Empty) return BadRequest("AccountId is required.");
        if (req.Amount    <= 0)          return BadRequest("Amount must be > 0.");
        try
        {
            var dto = await txService.AddAsync(req, ct);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }
}
