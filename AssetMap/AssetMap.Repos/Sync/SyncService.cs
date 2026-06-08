using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AssetMap.Repos.Accounts;

namespace AssetMap.Repos.Sync;

/// <summary>
/// Pozadí synchronizace — každých 30 s zkouší odeslat neuložené mutace na server.
/// </summary>
public static class SyncService
{
    // ── State ──────────────────────────────────────────────
    private static bool _isOnline;

    public static bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (_isOnline == value) return;
            _isOnline = value;
            IsOnlineChanged?.Invoke(value);
        }
    }

    /// <summary>Volá se při změně stavu připojení (z libovolného vlákna).</summary>
    public static event Action<bool>? IsOnlineChanged;

    /// <summary>Volá se po úspěšném odeslání všech mutací.</summary>
    public static event Action? SyncCompleted;

    // ── Config ─────────────────────────────────────────────
    /// <summary>Getter pro URL serveru — nastavit na AccountRepo.ServerUrl.</summary>
    public static Func<string> GetServerUrl { get; set; } = () => "http://localhost:5033";

    /// <summary>Getter pro API klíč — nastavit na AccountRepo.ApiKey.</summary>
    public static Func<string> GetApiKey    { get; set; } = () => "";

    // ── Timer ──────────────────────────────────────────────
    private static Timer? _timer;

    /// <summary>Spustí pravidelný sync. Bezpečné volat vícekrát.</summary>
    public static void Start()
    {
        _timer?.Dispose();
        _timer = new Timer(
            _ => _ = TryFlushAsync(),
            null,
            TimeSpan.FromSeconds(15),   // první pokus po 15 s
            TimeSpan.FromSeconds(30));  // pak každých 30 s
    }

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Nastaví stav online bez HTTP pingu (voláno z AccountRepo po úspěšném volání).</summary>
    public static void SetOnline(bool online) => IsOnline = online;

    // ── HTTP ───────────────────────────────────────────────
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static void ApplyAuthHeader()
    {
        _http.DefaultRequestHeaders.Remove("Authorization");
        string key = GetApiKey();
        if (!string.IsNullOrWhiteSpace(key))
            _http.DefaultRequestHeaders.Add("Authorization", $"ApiKey {key}");
    }

    // ── Ping ───────────────────────────────────────────────
    public static async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            ApplyAuthHeader();
            var resp = await _http.GetAsync(
                GetServerUrl().TrimEnd('/') + "/health", ct);
            IsOnline = resp.IsSuccessStatusCode;
        }
        catch { IsOnline = false; }
        return IsOnline;
    }

    // ── Flush ──────────────────────────────────────────────
    /// <summary>
    /// Pokusí se odeslat všechny čekající mutace.
    /// Vrátí true, pokud je fronta po dokončení prázdná.
    /// </summary>
    public static async Task<bool> TryFlushAsync(CancellationToken ct = default)
    {
        if (!PendingQueue.HasPending)
        {
            await PingAsync(ct);
            return true;
        }

        ApplyAuthHeader();
        string baseUrl  = GetServerUrl().TrimEnd('/');
        bool   allDone  = true;

        foreach (var mutation in PendingQueue.GetAll())
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                mutation.RetryCount++;

                HttpResponseMessage resp = mutation.HttpMethod switch
                {
                    "DELETE" => await _http.DeleteAsync(baseUrl + mutation.Endpoint, ct),
                    "PUT"    => await _http.PutAsync(
                                    baseUrl + mutation.Endpoint,
                                    new StringContent(mutation.Payload, Encoding.UTF8, "application/json"),
                                    ct),
                    _        => await _http.PostAsync(
                                    baseUrl + mutation.Endpoint,
                                    new StringContent(mutation.Payload, Encoding.UTF8, "application/json"),
                                    ct),
                };

                if (resp.IsSuccessStatusCode)
                {
                    PendingQueue.Remove(mutation.Id);
                    IsOnline = true;
                }
                else
                {
                    allDone = false;
                    // 4xx = client chyba (neplatný payload), nebudeme zkoušet donekonečna
                    if ((int)resp.StatusCode is >= 400 and < 500)
                        PendingQueue.Remove(mutation.Id);
                }
            }
            catch (HttpRequestException)
            {
                // Server nedostupný — přestat zkoušet zbytek fronty
                IsOnline = false;
                allDone  = false;
                break;
            }
        }

        // Pokud vše odesláno, načti aktuální data ze serveru
        if (allDone && !PendingQueue.HasPending)
        {
            IsOnline = true;
            await AccountRepo.RefreshAsync();
            SyncCompleted?.Invoke();
        }

        return allDone && !PendingQueue.HasPending;
    }
}
