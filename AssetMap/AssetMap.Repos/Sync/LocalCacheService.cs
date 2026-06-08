using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AssetMap.Repos.Sync;

/// <summary>
/// Ukládá poslední odpověď ze serveru (raw JSON) na disk.
/// Slouží jako offline fallback, pokud server není dostupný.
/// </summary>
public static class LocalCacheService
{
    // ── Config ─────────────────────────────────────────────
    public static string DataDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssetMap");

    private static string CachePath => Path.Combine(DataDir, "account-cache.json");
    private static string MetaPath  => Path.Combine(DataDir, "account-cache.meta.json");

    // ── State ──────────────────────────────────────────────
    public static DateTime? LastSaved { get; private set; }
    public static bool      Exists    => File.Exists(CachePath);

    // ── Save ───────────────────────────────────────────────
    /// <summary>Uloží raw JSON odpovědi ze serveru na disk.</summary>
    public static void SaveRaw(string json)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(CachePath, json);
            LastSaved = DateTime.Now;
            File.WriteAllText(MetaPath,
                JsonSerializer.Serialize(new { savedAt = LastSaved.Value.ToString("o") }));
        }
        catch { }
    }

    // ── Load ───────────────────────────────────────────────
    /// <summary>Načte raw JSON z disku. Null pokud soubor neexistuje nebo je poškozený.</summary>
    public static string? LoadRaw()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;

            // Načti timestamp z meta souboru
            if (File.Exists(MetaPath))
            {
                var meta = JsonNode.Parse(File.ReadAllText(MetaPath));
                if (meta?["savedAt"]?.GetValue<string>() is { } ts &&
                    DateTime.TryParse(ts, out var savedAt))
                    LastSaved = savedAt;
            }

            return File.ReadAllText(CachePath);
        }
        catch { return null; }
    }
}
