using AssetMap.Database;
using Microsoft.EntityFrameworkCore;

namespace AssetMap.API.Middleware;

/// <summary>
/// Autentizace přes API klíč — multi-user podpora.
///
/// Logika:
///   1. Extrahuje klíč z "Authorization: ApiKey {klíč}" hlavičky.
///   2. Hledá uživatele v DB podle ApiKey.
///      - Prázdná hlavička → hledá uživatele s ApiKey = null (dev mode).
///      - Platný klíč       → hledá přesnou shodu.
///   3. Nalezený uživatel: UserId uloží do HttpContext.Items["UserId"].
///   4. /health vždy projde (server alive check — klient testuje dostupnost).
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, IServiceProvider services)
{
    private const string HeaderName = "Authorization";
    private const string Prefix     = "ApiKey ";

    public async Task InvokeAsync(HttpContext ctx)
    {
        // /health vždy projde — slouží jen k ověření dostupnosti serveru
        if (ctx.Request.Path.StartsWithSegments("/health"))
        {
            await next(ctx);
            return;
        }

        // Extrahuj klíč z hlavičky
        string? providedKey = null;
        if (ctx.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            var raw = header.ToString();
            if (raw.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                providedKey = raw[Prefix.Length..].Trim();
        }

        // DB lookup
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        AssetMap.Entities.User? user;

        if (string.IsNullOrEmpty(providedKey))
        {
            // Žádný klíč — přijmeme jen uživatele s ApiKey = null (dev mode)
            user = await db.Users
                .FirstOrDefaultAsync(u => u.ApiKey == null);
        }
        else
        {
            // Klíč poskytnut — hledej přesnou shodu
            user = await db.Users
                .FirstOrDefaultAsync(u => u.ApiKey == providedKey);
        }

        if (user is null)
        {
            ctx.Response.StatusCode  = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(
                """{"error":"Unauthorized — invalid or missing ApiKey"}""");
            return;
        }

        // Ulož identitu do kontextu — controllery ji použijí místo hardcoded DefaultUserId
        ctx.Items["UserId"] = user.Id;
        await next(ctx);
    }
}
