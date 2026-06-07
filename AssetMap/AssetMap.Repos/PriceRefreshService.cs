using System;
using System.Threading;
using System.Threading.Tasks;
using AssetMap.Repos.Accounts;

namespace AssetMap.Repos;

/// <summary>
/// Pozadí služba pro periodické obnovování cen.
/// Crypto: každou hodinu.
/// Banky: při startu a po explicitním refresh (AccountRepo.RefreshAsync).
/// TODO: po připojení API rozdělit refresh na crypto vs. banky.
/// </summary>
public static class PriceRefreshService
{
    private static CancellationTokenSource? _cts;

    /// <summary>Spustí hodinový refresh v pozadí. Bezpečné volat vícekrát — předchozí instance se zastaví.</summary>
    public static void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                    await AccountRepo.RefreshAsync();
            }
            catch (OperationCanceledException) { /* normální ukončení */ }
        }, token);
    }

    /// <summary>Zastaví refresh (při zavření aplikace).</summary>
    public static void Stop() => _cts?.Cancel();
}
