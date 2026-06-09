using Microsoft.AspNetCore.Mvc;

namespace AssetMap.API.Controllers;

/// <summary>Dočasný debug kontroler — smazat před produkcí.</summary>
[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", "AssetMap/1.0" } },
    };

    /// <summary>
    /// Testuje blockchain sync pro zadanou adresu/xpub.
    /// GET /api/debug/bitcoin?addr=bc1q...
    /// GET /api/debug/bitcoin?addr=xpub6...
    /// GET /api/debug/bitcoin?addr=zpub...
    /// </summary>
    [HttpGet("bitcoin")]
    public async Task<IActionResult> TestBitcoin([FromQuery] string addr, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(addr))
            return BadRequest("Parametr 'addr' je povinný. Př: /api/debug/bitcoin?addr=bc1q...");

        addr = addr.Trim();
        bool isXpub = addr.StartsWith("xpub", StringComparison.OrdinalIgnoreCase) ||
                      addr.StartsWith("ypub", StringComparison.OrdinalIgnoreCase) ||
                      addr.StartsWith("zpub", StringComparison.OrdinalIgnoreCase);

        if (!isXpub)
        {
            // Single address → blockstream.info
            string url = $"https://blockstream.info/api/address/{Uri.EscapeDataString(addr)}/txs";
            try
            {
                using var resp = await _http.GetAsync(url, ct);
                string body    = await resp.Content.ReadAsStringAsync(ct);
                int txCount    = -1;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                        txCount = doc.RootElement.GetArrayLength();
                }
                catch { }

                return Ok(new
                {
                    mode       = "single-address → blockstream.info",
                    address    = addr,
                    url,
                    httpStatus = (int)resp.StatusCode,
                    txCount,
                    body       = body.Length > 3000 ? body[..3000] + "…(zkráceno)" : body,
                });
            }
            catch (Exception ex)
            {
                return Ok(new { mode = "single-address", address = addr, url, error = ex.Message });
            }
        }

        // xpub/ypub/zpub → testuj všechny tři script typy, prvních 20 adres
        try
        {
            string xpubNorm = addr;
            if (addr.StartsWith("zpub", StringComparison.OrdinalIgnoreCase) ||
                addr.StartsWith("ypub", StringComparison.OrdinalIgnoreCase))
            {
                var data = NBitcoin.DataEncoders.Encoders.Base58Check.DecodeData(addr);
                new byte[] { 0x04, 0x88, 0xB2, 0x1E }.CopyTo(data, 0);
                xpubNorm = NBitcoin.DataEncoders.Encoders.Base58Check.EncodeData(data);
            }

            var extKey = NBitcoin.ExtPubKey.Parse(xpubNorm, NBitcoin.Network.Main);
            var chainKey = extKey.Derive(0u); // external chain m/0/i

            // Testujeme všechny 3 script typy — mnohé peněženky exportují xpub ale používají Segwit
            var scriptTypes = new[]
            {
                (NBitcoin.ScriptPubKeyType.Segwit,     "BIP84 Segwit     (bc1q...)"),
                (NBitcoin.ScriptPubKeyType.SegwitP2SH, "BIP49 P2SH-SegWit (3...)  "),
                (NBitcoin.ScriptPubKeyType.Legacy,      "BIP44 Legacy      (1...)  "),
            };

            int count = int.TryParse(Request.Query["n"], out int n) ? Math.Clamp(n, 1, 50) : 20;
            var allResults = new List<object>();

            foreach (var (scriptType, scriptLabel) in scriptTypes)
            {
                var rows = new List<object>();
                int totalTx = 0;

                for (int i = 0; i < count; i++)
                {
                    string derivedAddr = chainKey.Derive((uint)i)
                        .PubKey.GetAddress(scriptType, NBitcoin.Network.Main).ToString();

                    string bsUrl = $"https://blockstream.info/api/address/{derivedAddr}/txs";
                    int txCount  = 0;
                    try
                    {
                        using var r   = await _http.GetAsync(bsUrl, ct);
                        string bsBody = await r.Content.ReadAsStringAsync(ct);
                        using var doc = System.Text.Json.JsonDocument.Parse(bsBody);
                        txCount = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                            ? doc.RootElement.GetArrayLength() : 0;
                    }
                    catch { }

                    totalTx += txCount;
                    rows.Add(new { path = $"m/0/{i}", address = derivedAddr, txCount });
                    await Task.Delay(80, ct);
                }

                allResults.Add(new { scriptType = scriptLabel, totalTxFound = totalTx, addresses = rows });
            }

            return Ok(new
            {
                mode     = "xpub → NBitcoin + blockstream.info, všechny script typy",
                inputKey = addr.Length > 20 ? addr[..20] + "…" : addr,
                checkedAddressesPerType = count,
                tip      = "Hledej který scriptType má totalTxFound > 0 — to je správný typ pro sync.",
                results  = allResults,
            });
        }
        catch (Exception ex)
        {
            return Ok(new { mode = "xpub", error = $"Chyba parsování klíče: {ex.Message}" });
        }
    }
}
