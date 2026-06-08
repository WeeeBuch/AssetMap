using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AssetMap.Repos.Sync;

/// <summary>
/// Ukládá poslední odpověď ze serveru (raw JSON) na disk.
/// Slouží jako offline fallback, pokud server není dostupný.
///
/// Šifrování: Windows DPAPI (ProtectedData) — klíč vázaný na aktuálního
/// Windows uživatele (CurrentUser scope). Fallback na plaintext na jiných OS.
/// </summary>
public static class LocalCacheService
{
    // ── Config ─────────────────────────────────────────────
    public static string DataDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssetMap");

    private static string CachePath => Path.Combine(DataDir, "account-cache.bin");
    private static string MetaPath  => Path.Combine(DataDir, "account-cache.meta.json");

    // Zpětná kompatibilita — starý plaintext soubor
    private static string LegacyPath => Path.Combine(DataDir, "account-cache.json");

    // ── State ──────────────────────────────────────────────
    public static DateTime? LastSaved { get; private set; }
    public static bool      Exists    => File.Exists(CachePath) || File.Exists(LegacyPath);

    // ── Save ───────────────────────────────────────────────
    /// <summary>Uloží raw JSON odpovědi ze serveru na disk (šifrovaně).</summary>
    public static void SaveRaw(string json)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);
            byte[] stored     = Encrypt(plainBytes);
            File.WriteAllBytes(CachePath, stored);
            LastSaved = DateTime.Now;
            File.WriteAllText(MetaPath,
                JsonSerializer.Serialize(new
                {
                    savedAt   = LastSaved.Value.ToString("o"),
                    encrypted = EncryptionAvailable,
                }));

            // Smaž starý plaintext soubor pokud existuje
            if (File.Exists(LegacyPath)) File.Delete(LegacyPath);
        }
        catch { }
    }

    // ── Load ───────────────────────────────────────────────
    /// <summary>Načte raw JSON z disku. Null pokud soubor neexistuje nebo je poškozený.</summary>
    public static string? LoadRaw()
    {
        try
        {
            // Nejdřív zkus nový šifrovaný soubor
            if (File.Exists(CachePath))
            {
                if (File.Exists(MetaPath))
                {
                    var meta = JsonNode.Parse(File.ReadAllText(MetaPath));
                    if (meta?["savedAt"]?.GetValue<string>() is { } ts &&
                        DateTime.TryParse(ts, out var savedAt))
                        LastSaved = savedAt;
                }

                byte[] stored = File.ReadAllBytes(CachePath);
                byte[] plain  = Decrypt(stored);
                return Encoding.UTF8.GetString(plain);
            }

            // Fallback: starý plaintext soubor (migrace)
            if (File.Exists(LegacyPath))
                return File.ReadAllText(LegacyPath);

            return null;
        }
        catch { return null; }
    }

    // ── Šifrování (DPAPI / plain fallback) ─────────────────
    private static bool EncryptionAvailable =>
        OperatingSystem.IsWindows();

    private static byte[] Encrypt(byte[] data)
    {
        if (OperatingSystem.IsWindows())
            return DpapiEncrypt(data);
        return data; // plaintext na non-Windows
    }

    private static byte[] Decrypt(byte[] data)
    {
        if (OperatingSystem.IsWindows())
            return DpapiDecrypt(data);
        return data;
    }

    [SupportedOSPlatform("windows")]
    private static byte[] DpapiEncrypt(byte[] data) =>
        ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] DpapiDecrypt(byte[] data)
    {
        try
        {
            return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
        }
        catch
        {
            // Data nejsou DPAPI šifrovaná (starý formát) — vrať as-is
            return data;
        }
    }
}
