using System.Diagnostics;
using System.Net;
using Microsoft.Data.Sqlite;

namespace GSKit.Core.Http;

/// <summary>
/// Thin HTTP wrapper that mimics Chrome's request fingerprint well enough
/// to pass Cloudflare's managed challenge on residential IPs.
///
/// From HAR analysis (2026-09-27):
///   - Stores-FindStores returns 200 with ZERO cookies from a residential IP.
///   - CF is scoring TLS fingerprint + header order only — no session, no cookies.
///   - Tile-GetProductsJSON, Stores-ProductDetailStoreAvailability, Stores-InventorySearch
///     all 403 from datacenter IPs regardless of headers — residential only.
///   - Constructor.io search (ac.cnstrc.com) is a separate domain, no CF, works anywhere.
///
/// Session/cookie machinery was removed: SetPreferredStore POST 403s anyway
/// and FindStores works cookieless confirmed via HAR.
/// </summary>
public sealed class GameStopClient : IAsyncDisposable
{
    private const string Base     = "https://www.gamestop.com";
    private const string SiteId   = "Sites-gamestop-us-Site";
    private const string SfccBase = $"{Base}/on/demandware.store/{SiteId}/default";

    private readonly HttpClient _http;
    private readonly bool       _debug;

    public GameStopClient(bool debug = false)
    {
        _debug = debug;

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect      = true,
            UseCookies             = false,   // intentional — CF doesn't need them for FindStores
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>
    /// GET a SFCC controller action with Chrome-equivalent headers.
    /// Pass withCookies=true to inject cf_clearance from Chrome for CF-challenged endpoints.
    /// </summary>
    public async Task<HttpResponseMessage> SfccGetAsync(
        string controllerAction,
        string queryString   = "",
        string? refererSku   = null,
        bool   withCookies   = false,
        CancellationToken ct = default)
    {
        var url     = $"{SfccBase}/{controllerAction}?{queryString}";
        var referer = refererSku is not null
            ? $"{Base}/products/{refererSku}"
            : $"{Base}/trade/";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddBrowserHeaders(req, referer);

        if (withCookies)
        {
            var cookies = ChromeCookieReader.GetGameStopCookies(debug: _debug);
            if (cookies is { Length: > 0 })
            {
                req.Headers.TryAddWithoutValidation("cookie", cookies);
                if (_debug) Console.Error.WriteLine($"[DBG] injected Chrome cookies");
            }
            else if (_debug)
                Console.Error.WriteLine("[DBG] no Chrome cookies found for gamestop.com");
        }

        if (_debug) Console.Error.WriteLine($"[DBG] GET {url}");

        var sw  = Stopwatch.StartNew();
        var res = await _http.SendAsync(req, ct);
        sw.Stop();

        if (_debug)
            Console.Error.WriteLine(
                $"[DBG] {(int)res.StatusCode} {res.StatusCode}  {sw.ElapsedMilliseconds}ms");

        return res;
    }

    private static void AddBrowserHeaders(HttpRequestMessage req, string referer)
    {
        req.Headers.TryAddWithoutValidation("accept",          "application/json, text/javascript, */*; q=0.01");
        req.Headers.TryAddWithoutValidation("accept-encoding", "gzip, deflate, br, zstd");
        req.Headers.TryAddWithoutValidation("accept-language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("referer",         referer);
        req.Headers.TryAddWithoutValidation("sec-ch-ua",
            "\"Google Chrome\";v=\"153\", \"Not_A Brand\";v=\"8\", \"Chromium\";v=\"153\"");
        req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile",   "?0");
        req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"macOS\"");
        req.Headers.TryAddWithoutValidation("sec-fetch-dest",     "empty");
        req.Headers.TryAddWithoutValidation("sec-fetch-mode",     "cors");
        req.Headers.TryAddWithoutValidation("sec-fetch-site",     "same-origin");
        req.Headers.TryAddWithoutValidation("user-agent",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("x-requested-with", "XMLHttpRequest");
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await Task.CompletedTask;
    }
}

public class CloudflareBlockException(string message) : Exception(message);

/// <summary>
/// Reads cf_clearance + __cq_seg cookies from Chrome's SQLite cookie store on macOS.
/// These are required for SFCC endpoints that CF challenges even on residential IPs
/// (Trade-GetSuggestions, Trade-Show). Decrypts AES-GCM v10/v20 via macOS Keychain.
/// </summary>
public static class ChromeCookieReader
{
    private const string ChromeCookiePath =
        "/Users/{0}/Library/Application Support/Google/Chrome/Default/Cookies";

    public static string? GetGameStopCookies(bool debug = false)
    {
        try
        {
            var user = Environment.UserName;
            var path = string.Format(ChromeCookiePath, user);
            if (!File.Exists(path))
            {
                if (debug) Console.Error.WriteLine($"[DBG] Chrome cookie DB not found at: {path}");
                return null;
            }

            // Copy DB + WAL/SHM files so we can open a consistent snapshot while Chrome runs
            var tmp = Path.Combine(Path.GetTempPath(), $"gskit_cookies_{Guid.NewGuid():N}.db");
            File.Copy(path, tmp, overwrite: true);
            foreach (var ext in new[] { "-wal", "-shm" })
            {
                var src2 = path + ext;
                if (File.Exists(src2)) File.Copy(src2, tmp + ext, overwrite: true);
            }

            var key = GetChromeEncryptionKey(debug);
            if (key is null)
            {
                if (debug) Console.Error.WriteLine("[DBG] Could not get Chrome encryption key from Keychain");
                File.Delete(tmp);
                return null;
            }

            var cookies = new List<string>();
            // immutable=1 prevents SQLite from trying to lock the copied file
            using var conn = new SqliteConnection($"Data Source={tmp};Mode=ReadOnly;Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            // Grab cf_clearance and common SFCC session cookies for gamestop.com
            cmd.CommandText = @"
                SELECT name, encrypted_value
                FROM cookies
                WHERE host_key LIKE '%gamestop.com'
                  AND (   name = 'cf_clearance'
                       OR name LIKE '__cq%'
                       OR name LIKE 'dwanonymous%'
                       OR name = 'sid'
                       OR name = 'dwsid')";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                var enc  = (byte[])reader[1];
                var val  = DecryptChromeValue(enc, key);
                if (debug) Console.Error.WriteLine($"[DBG] cookie {name}: {(val is null ? "DECRYPT_FAILED" : $"{val[..Math.Min(20,val.Length)]}…")}");
                if (val is { Length: > 0 }) cookies.Add($"{name}={val}");
            }
            conn.Close();

            // Cleanup temp files
            foreach (var ext in new[] { "", "-wal", "-shm" })
                try { File.Delete(tmp + ext); } catch { }

            if (debug) Console.Error.WriteLine($"[DBG] found {cookies.Count} gamestop.com cookie(s) in Chrome");
            return cookies.Count > 0 ? string.Join("; ", cookies) : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DBG] ChromeCookieReader failed: {ex.Message}");
            return null;
        }
    }

    private static byte[]? GetChromeEncryptionKey(bool debug = false)
    {
        try
        {
            // macOS: key stored in Keychain as "Chrome Safe Storage"
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = "security",
                Arguments              = "find-generic-password -wa Chrome",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var pass = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            if (pass.Length == 0) return null;

            // PBKDF2-SHA1: password=ChromeSafeStorageKey, salt="saltysalt", iter=1003, keylen=16
            return System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                System.Text.Encoding.UTF8.GetBytes(pass),
                System.Text.Encoding.UTF8.GetBytes("saltysalt"),
                1003,
                System.Security.Cryptography.HashAlgorithmName.SHA1,
                16);
        }
        catch { return null; }
    }

    private static string? DecryptChromeValue(byte[] enc, byte[] key)
    {
        try
        {
            // v10/v20 prefix: 3 bytes version tag + 12 bytes nonce + ciphertext + 16 bytes tag
            if (enc.Length < 3) return null;
            var prefix = System.Text.Encoding.ASCII.GetString(enc, 0, 3);
            if (prefix is "v10" or "v20")
            {
                var nonce      = enc[3..15];
                var ciphertext = enc[15..];
                using var aes  = new System.Security.Cryptography.AesGcm(key, 16);
                var plain      = new byte[ciphertext.Length - 16];
                var tag        = ciphertext[^16..];
                var cipher     = ciphertext[..^16];
                aes.Decrypt(nonce, cipher, tag, plain);
                return System.Text.Encoding.UTF8.GetString(plain);
            }
            // Fallback: old DPAPI (Windows only, shouldn't hit on Mac)
            return null;
        }
        catch { return null; }
    }
}
