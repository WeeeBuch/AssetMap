using AssetMap.API.BackgroundServices;
using AssetMap.API.Middleware;
using AssetMap.Comms;
using AssetMap.Core;
using AssetMap.Database;
using Microsoft.EntityFrameworkCore;

namespace AssetMap.API;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ── Konfigurace ───────────────────────────────────────────
        // Env proměnné přepíší appsettings.json (ASSETMAP_API_KEY, DB_PROVIDER, DB_CONNECTION)
        builder.Configuration.AddEnvironmentVariables();

        // ── Databáze ──────────────────────────────────────────────
        string provider   = builder.Configuration["DB_PROVIDER"] is { Length: > 0 } p ? p : "sqlite";
        string connString = builder.Configuration["DB_CONNECTION"] is { Length: > 0 } c ? c
                            : $"Data Source={Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                ".assetmap", "data.db")}";

        builder.Services.AddDbContext<AppDbContext>(opts =>
        {
            if (provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
                opts.UseNpgsql(connString);
            else
                opts.UseSqlite(connString);
        });

        // ── Služby ────────────────────────────────────────────────
        builder.Services.AddCommsServices();
        builder.Services.AddCoreServices();
        builder.Services.AddHostedService<SnapshotHostedService>();

        // ── ASP.NET Core ──────────────────────────────────────────
        builder.Services.AddControllers();
        builder.Services.AddOpenApi();

        // CORS — Avalonia běží lokálně, ale pro dev povolíme vše
        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        var app = builder.Build();

        // ── Migrace při startu ────────────────────────────────────
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Vytvoří složku ~/.assetmap pokud neexistuje (SQLite)
            if (provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(connString.Replace("Data Source=", ""));
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }
            await db.Database.MigrateAsync();
        }

        // ── Pipeline ──────────────────────────────────────────────
        if (app.Environment.IsDevelopment())
            app.MapOpenApi();

        app.UseCors();
        app.UseMiddleware<ApiKeyMiddleware>();
        app.UseAuthorization();
        app.MapControllers();

        await app.RunAsync();
    }
}
