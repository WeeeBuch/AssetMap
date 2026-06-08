using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace AssetMap.Repos.Sync;

/// <summary>
/// Fronta čekajících mutací — přetrvává napříč restarty (pending.bin).
/// Thread-safe. Šifrováno přes DPAPI na Windows, plaintext fallback jinak.
/// </summary>
public static class PendingQueue
{
    // ── Config ─────────────────────────────────────────────
    public static string DataDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssetMap");

    private static string FilePath       => Path.Combine(DataDir, "pending.bin");
    private static string LegacyFilePath => Path.Combine(DataDir, "pending.json");

    // ── State ──────────────────────────────────────────────
    private static readonly Lock _lock = new();
    private static List<PendingMutation> _queue = [];

    public static bool HasPending { get { lock (_lock) return _queue.Count > 0; } }
    public static int  Count      { get { lock (_lock) return _queue.Count;     } }

    /// <summary>Vyvolá se po každé změně fronty (z libovolného vlákna).</summary>
    public static event Action? Changed;

    static PendingQueue() => Load();

    // ── API ────────────────────────────────────────────────
    public static void Enqueue(PendingMutation mutation)
    {
        lock (_lock)
        {
            _queue.Add(mutation);
            Save();
        }
        Changed?.Invoke();
    }

    public static void Remove(Guid id)
    {
        lock (_lock)
        {
            _queue.RemoveAll(m => m.Id == id);
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>Snapshot fronty (kopie — bezpečné iterovat bez locku).</summary>
    public static IReadOnlyList<PendingMutation> GetAll()
    {
        lock (_lock)
            return [.. _queue];
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
            Save();
        }
        Changed?.Invoke();
    }

    // ── Persistence ────────────────────────────────────────
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    private static void Load()
    {
        try
        {
            // Nový šifrovaný soubor
            if (File.Exists(FilePath))
            {
                byte[] stored = File.ReadAllBytes(FilePath);
                byte[] plain  = Decrypt(stored);
                string text   = Encoding.UTF8.GetString(plain);
                _queue = JsonSerializer.Deserialize<List<PendingMutation>>(text, _json) ?? [];
                return;
            }

            // Fallback: starý plaintext soubor (migrace)
            if (File.Exists(LegacyFilePath))
            {
                string text = File.ReadAllText(LegacyFilePath);
                _queue = JsonSerializer.Deserialize<List<PendingMutation>>(text, _json) ?? [];
                // Migrace: přepiš šifrovaně
                Save();
                File.Delete(LegacyFilePath);
            }
        }
        catch { _queue = []; }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            byte[] plain  = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_queue, _json));
            byte[] stored = Encrypt(plain);
            File.WriteAllBytes(FilePath, stored);
        }
        catch { /* disk chyba — ignorovat */ }
    }

    // ── Šifrování (DPAPI / plain fallback) ─────────────────
    private static byte[] Encrypt(byte[] data)
    {
        if (OperatingSystem.IsWindows())
            return DpapiEncrypt(data);
        return data;
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
            // Nejsou DPAPI šifrovaná (starý / plaintext formát) — vrať as-is
            return data;
        }
    }
}
