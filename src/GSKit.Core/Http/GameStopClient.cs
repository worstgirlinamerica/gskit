using System.Diagnostics;
using System.Net;

namespace GSKit.Core.Http;

/// <summary>
/// Thin HTTP wrapper that mimics Chrome's request fingerprint well enough
/// to pass Cloudflare's managed challenge on residential IPs.
///
/// From HAR analysis (2026-09-27): Stores-FindStores returns 200 with ZERO
/// cookies from a residential IP for the basic query. CF is scoring TLS
/// fingerprint + header order only for that endpoint.
///
/// However, selectedStore overrides require a dwsid session cookie so
/// GameStop's backend knows which store to use. We init a session by hitting
/// the homepage first, which sets dwsid + dwanonymous_ cookies, then use
/// Stores-UpdateInStoreShipmentID to pin the store into that session.
///
/// What matters for CF:
///   1. Header order matches Chrome wire order from HAR exactly
///   2. Accept-Encoding includes br + zstd
///   3. X-Requested-With: XMLHttpRequest on AJAX calls
///   4. Referer set to the product page
///   5. Cookies enabled so session flows correctly
/// </summary>
public sealed class GameStopClient : IAsyncDisposable
{
    private const string Base     = "https://www.gamestop.com";
    private const string SiteId   = "Sites-gamestop-us-Site";
    private const string SfccBase = $"{Base}/on/demandware.store/{SiteId}/default";

    private readonly HttpClient        _http;
    private readonly CookieContainer   _cookies;
    private readonly bool              _debug;
    private bool                       _sessionInit = false;

    public GameStopClient(bool debug = false)
    {
        _debug   = debug;
        _cookies = new CookieContainer();

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect      = true,
            UseCookies             = true,
            CookieContainer        = _cookies,
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>
    /// Hit the homepage to get a dwsid + dwanonymous_ session cookie.
    /// Only needed before selectedStore operations. Safe to call multiple times.
    /// </summary>
    public async Task EnsureSessionAsync(CancellationToken ct = default)
    {
        if (_sessionInit) return;

        if (_debug) Console.Error.WriteLine("[DBG] initialising session (GET homepage)");

        using var req = new HttpRequestMessage(HttpMethod.Get, Base + "/");
        AddBrowserHeaders(req, referer: Base + "/");
        // Homepage get uses a broader accept
        req.Headers.Remove("accept");
        req.Headers.TryAddWithoutValidation("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        req.Headers.Remove("x-requested-with");

        var res = await _http.SendAsync(req, ct);
        if (_debug)
            Console.Error.WriteLine($"[DBG] session init  {(int)res.StatusCode}  cookies: {_cookies.Count}");

        _sessionInit = true;
    }

    /// <summary>
    /// POST Stores-UpdateInStoreShipmentID to pin a store into the current session.
    /// Must call EnsureSessionAsync first.
    /// </summary>
    public async Task SetPreferredStoreAsync(string storeId, string sku, CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct);

        var url = $"{SfccBase}/Stores-UpdateInStoreShipmentID";
        if (_debug) Console.Error.WriteLine($"[DBG] POST {url}  storeId={storeId}");

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        AddBrowserHeaders(req, referer: $"{Base}/products/{sku}");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["storeId"]   = storeId,
            ["pid"]       = sku,
            ["quantity"]  = "1",
        });

        var res  = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (_debug)
            Console.Error.WriteLine($"[DBG] SetPreferredStore  {(int)res.StatusCode}  body: {body[..Math.Min(200, body.Length)]}");
    }

    /// <summary>
    /// Fire a GET at a SFCC controller action with Chrome-equivalent headers.
    /// </summary>
    public async Task<HttpResponseMessage> SfccGetAsync(
        string controllerAction,
        string queryString   = "",
        string? refererSku   = null,
        CancellationToken ct = default)
    {
        var url     = $"{SfccBase}/{controllerAction}?{queryString}";
        var referer = refererSku is not null
            ? $"{Base}/products/{refererSku}"
            : $"{Base}/";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddBrowserHeaders(req, referer);

        if (_debug)
            Console.Error.WriteLine($"[DBG] GET {url}");

        var sw  = Stopwatch.StartNew();
        var res = await _http.SendAsync(req, ct);
        sw.Stop();

        if (_debug)
            Console.Error.WriteLine(
                $"[DBG] {(int)res.StatusCode} {res.StatusCode}  {sw.ElapsedMilliseconds}ms" +
                $"  content-length={res.Content.Headers.ContentLength?.ToString() ?? "?"}");

        return res;
    }

    private static void AddBrowserHeaders(HttpRequestMessage req, string referer)
    {
        req.Headers.TryAddWithoutValidation("accept", "application/json, text/javascript, */*; q=0.01");
        req.Headers.TryAddWithoutValidation("accept-encoding", "gzip, deflate, br, zstd");
        req.Headers.TryAddWithoutValidation("accept-language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("referer", referer);
        req.Headers.TryAddWithoutValidation("sec-ch-ua",
            "\"Google Chrome\";v=\"153\", \"Not_A Brand\";v=\"8\", \"Chromium\";v=\"153\"");
        req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"macOS\"");
        req.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
        req.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
        req.Headers.TryAddWithoutValidation("sec-fetch-site", "same-origin");
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
