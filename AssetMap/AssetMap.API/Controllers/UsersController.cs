using AssetMap.Database;
using AssetMap.Entities;
using AssetMap.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace AssetMap.API.Controllers;

/// <summary>
/// Správa uživatelů — přidávání, generování API klíčů.
/// Vyžaduje přihlášeného uživatele s rolí Admin.
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController(AppDbContext db) : ControllerBase
{
    private Guid CallerUserId => HttpContext.Items["UserId"] is Guid id ? id
        : Guid.Parse("00000000-0000-0000-0000-000000000001");

    // ── GET /api/users ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        if (!await IsAdminAsync(ct)) return Forbid();

        var users = await db.Users
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Role,
                u.BaseCurrency,
                u.CreatedAt,
                HasApiKey = u.ApiKey != null,
            })
            .ToListAsync(ct);
        return Ok(users);
    }

    // ── POST /api/users ────────────────────────────────────────────
    /// <summary>Vytvoří nového uživatele a okamžitě vygeneruje API klíč.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserRequest req, CancellationToken ct)
    {
        if (!await IsAdminAsync(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Username)) return BadRequest("Username is required.");

        bool exists = await db.Users.AnyAsync(u => u.Username == req.Username, ct);
        if (exists) return Conflict($"User '{req.Username}' already exists.");

        string apiKey = GenerateApiKey();
        var user = new User
        {
            Id           = Guid.NewGuid(),
            Username     = req.Username.Trim(),
            PasswordHash = "",
            ApiKey       = apiKey,
            BaseCurrency = req.BaseCurrency ?? "USD",
            Role         = UserRole.Member,
            CreatedAt    = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return Ok(new { user.Id, user.Username, ApiKey = apiKey });
    }

    // ── POST /api/users/{id}/generate-key ─────────────────────────
    /// <summary>Vygeneruje nový API klíč pro daného uživatele (přepíše starý).</summary>
    [HttpPost("{id:guid}/generate-key")]
    public async Task<IActionResult> GenerateKey(Guid id, CancellationToken ct)
    {
        if (!await IsAdminAsync(ct) && CallerUserId != id) return Forbid();

        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        user.ApiKey = GenerateApiKey();
        await db.SaveChangesAsync(ct);
        return Ok(new { ApiKey = user.ApiKey });
    }

    // ── POST /api/users/generate-key  (pro seed usera) ────────────
    /// <summary>Zástupce — vygeneruje klíč pro aktuálně přihlášeného uživatele.</summary>
    [HttpPost("generate-key")]
    public async Task<IActionResult> GenerateMyKey(CancellationToken ct)
        => await GenerateKey(CallerUserId, ct);

    // ── DELETE /api/users/{id} ─────────────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!await IsAdminAsync(ct)) return Forbid();

        var seedId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        if (id == seedId) return BadRequest("Seed uživatele nelze smazat.");

        int rows = await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0 ? NoContent() : NotFound();
    }

    // ── Helpers ────────────────────────────────────────────────────
    private async Task<bool> IsAdminAsync(CancellationToken ct)
    {
        var user = await db.Users.FindAsync([CallerUserId], ct);
        return user?.Role == UserRole.Admin;
    }

    private static string GenerateApiKey()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
               .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

public record CreateUserRequest(string Username, string? BaseCurrency);
