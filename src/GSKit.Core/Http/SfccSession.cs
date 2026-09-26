using System.Net;
using System.Text.RegularExpressions;
using GSKit.Core.Auth;

namespace GSKit.Core.Http;

public sealed class SfccSession : IAsyncDisposable
{
    private const string Base     = "https://www.gamestop.com";
    private const string SiteId   = "Sites-gamestop-us-Site";
    private const string SfccBase = $"{Base}/on/demandware.store/{SiteId}/default";

    private readonly HttpClient      _http;
    private readonly CookieContainer _cookies;

    public string? DwSessionId { get; private set; }
    public string? CfClearance { get; private set; }
    public string? CsrfToken   { get; private set; }
    public bool    IsSeeded     => DwSessionId != null;

    public SfccSession()
    {
        _cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer        = _cookies,
            AllowAutoRedirect      = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        _http.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        _http.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
        _http.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
    }

    public async Task SeedFromChromeAsync(CancellationToken ct = default)
    {
        var cookies = await ChromeCookieReader.GetGameStopCookiesAsync();
        foreach (var kvp in cookies)
            InjectCookie(kvp.Key, kvp.Value);
        DwSessionId = cookies.GetValueOrDefault("dwsid");
        CfClearance = cookies.GetValueOrDefault("cf_clearance");
        if (DwSessionId == null)
            throw new InvalidOperationException(
                "No dwsid in Chrome cookies. Open gamestop.com in Chrome first.");
    }

    public void SeedManual(string dwsid, string cfClearance, string? csrf = null)
    {
        InjectCookie("dwsid",        dwsid);
        InjectCookie("cf_clearance", cfClearance);
        DwSessionId = dwsid;
        CfClearance = cfClearance;
        CsrfToken   = csrf;
    }

    public async Task SeedColdAsync(string sku, CancellationToken ct = default)
    {
        var html = await GetStringAsync($"{Base}/products/{sku}.html", ct);
        var m = Regex.Match(html,
            @"<meta\s+name=['""]csrf-token['""]\s+content=['""]([^'""]+)['""]");
        if (m.Success) CsrfToken = m.Groups[1].Value;
        DwSessionId = _cookies.GetCookies(new Uri(Base))["dwsid"]?.Value;
    }

    public async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return await _http.SendAsync(req, ct);
    }

    public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        var res = await GetAsync(url, ct);
        return await res.Content.ReadAsStringAsync(ct);
    }

    public Task<HttpResponseMessage> SfccGetAsync(
        string controllerAction, string queryString = "", CancellationToken ct = default)
        => GetAsync($"{SfccBase}/{controllerAction}?{queryString}", ct);

    private void InjectCookie(string name, string value) =>
        _cookies.Add(new Cookie(name, value, "/", ".gamestop.com"));

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await Task.CompletedTask;
    }
}
