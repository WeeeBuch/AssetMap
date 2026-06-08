using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace AssetMap.Repos.Sync;

/// <summary>
/// Fronta čekajících mutací — přetrvává napříč restarty (pending.json).
/// Thread-safe.
/// </summary>
public static class PendingQueue
{
    // ── Config ─────────────────────────────────────────────
    public static string DataDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssetMap");

    private static string FilePath => Path.Combine(DataDir, "pending.json");

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
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    private static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                _queue = JsonSerializer.Deserialize<List<PendingMutation>>(text, _json) ?? [];
            }
        }
        catch { _queue = []; }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_queue, _json));
        }
        catch { /* disk chyba — ignorovat */ }
    }
}
