using System.Diagnostics;
using System.Net;

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
///   - Trade-GetSuggestions returns an HTML partial cookieless — no CF, no session needed.
///
/// No cookie/session machinery needed. All endpoints work cookieless from residential IPs.
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
            UseCookies             = false,
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>
    /// GET a SFCC controller action with Chrome-equivalent headers.
    /// </summary>
    public async Task<HttpResponseMessage> SfccGetAsync(
        string            controllerAction,
        string            queryString = "",
        string?           refererSku  = null,
        CancellationToken ct          = default)
    {
        var url     = $"{SfccBase}/{controllerAction}?{queryString}";
        var referer = refererSku is not null
            ? $"{Base}/products/{refererSku}"
            : $"{Base}/trade/";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddBrowserHeaders(req, referer);

        if (_debug) Console.Error.WriteLine($"[DBG] GET {url}");

        var sw  = Stopwatch.StartNew();
        var res = await _http.SendAsync(req, ct);
        sw.Stop();

        if (_debug)
            Console.Error.WriteLine(
                $"[DBG] {(int)res.StatusCode} {res.StatusCode}  {sw.ElapsedMilliseconds}ms");

        return res;
    }

    // Overload for callers that previously passed withCookies — ignored now, kept for compat
    public Task<HttpResponseMessage> SfccGetAsync(
        string            controllerAction,
        string            queryString,
        string?           refererSku,
        bool              withCookies,
        CancellationToken ct)
        => SfccGetAsync(controllerAction, queryString, refererSku, ct);

    private static void AddBrowserHeaders(HttpRequestMessage req, string referer)
    {
        req.Headers.TryAddWithoutValidation("accept",          "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
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
