namespace AssetMap.API.Middleware;

/// <summary>
/// Vyžaduje hlavičku "Authorization: ApiKey {key}" na všech endpointech kromě /health.
/// Klíč se nastavuje env proměnnou ASSETMAP_API_KEY nebo v appsettings.json → ApiKey.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
{
    private const string HeaderName = "Authorization";
    private const string Prefix     = "ApiKey ";

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Health check nepotřebuje auth
        if (ctx.Request.Path.StartsWithSegments("/health"))
        {
            await next(ctx);
            return;
        }

        string? configured = config["ApiKey"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            // Pokud klíč není nastaven, API je veřejně přístupné (development mode)
            await next(ctx);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue(HeaderName, out var value) ||
            !value.ToString().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
            value.ToString()[Prefix.Length..] != configured)
        {
            ctx.Response.StatusCode  = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("""{"error":"Unauthorized — invalid or missing ApiKey"}""");
            return;
        }

        await next(ctx);
    }
}
