using System.Net;
using System.Text.Json;
using GSKit.Core.Models;

namespace GSKit.Core.Http;

/// <summary>
/// Client for Constructor.io's search API, which powers GameStop's site search.
///
/// API key discovered in HAR (2026-09-27): key_FIW9YAimY77z5QEf
/// Baked into gamestop's constructorClient.js bundle — not a secret, public key.
/// Endpoint: ac.cnstrc.com — separate domain, no Cloudflare, works from any IP.
///
/// Params confirmed via HAR behavioral events:
///   c=ciojs-client-2.1472.1  (client version string, required — omitting it 400s)
///   section=Products          (their catalog section name)
///   num_results_per_page=N    (NOT num_results — that 400s)
///   filters[platform]=X       (optional facet filter)
///   filters[condition]=X      (optional facet filter)
/// </summary>
public sealed class ConstructorClient : IAsyncDisposable
{
    private const string Base    = "https://ac.cnstrc.com";
    private const string ApiKey  = "key_FIW9YAimY77z5QEf";
    private const string Section = "Products";
    private const string CioJs   = "ciojs-client-2.1472.1";

    private readonly HttpClient _http;
    private readonly bool       _debug;

    public ConstructorClient(bool debug = false)
    {
        _debug = debug;
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("accept", "application/json");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("accept-language", "en-US,en;q=0.9");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("user-agent",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
    }

    public async Task<(List<SearchResult> Results, int Total)> SearchAsync(
        string query,
        int    page               = 1,
        int    resultsPerPage     = 10,
        string? filterPlatform   = null,
        string? filterCondition  = null,
        CancellationToken ct     = default)
    {
        var encoded = Uri.EscapeDataString(query);
        var qs = $"key={ApiKey}&section={Section}&num_results_per_page={resultsPerPage}&page={page}&c={CioJs}";

        if (filterPlatform  is { Length: > 0 })
            qs += $"&filters%5Bplatform%5D={Uri.EscapeDataString(filterPlatform)}";
        if (filterCondition is { Length: > 0 })
            qs += $"&filters%5Bcondition%5D={Uri.EscapeDataString(filterCondition)}";

        var url = $"{Base}/search/{encoded}?{qs}";

        if (_debug) Console.Error.WriteLine($"[DBG] GET {url}");

        var res  = await _http.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Constructor search returned {(int)res.StatusCode}: {body[..Math.Min(200, body.Length)]}");

        using var doc   = JsonDocument.Parse(body);
        var       resp  = doc.RootElement.GetProperty("response");
        var       total = resp.GetProperty("total_num_results").GetInt32();
        var       items = new List<SearchResult>();

        foreach (var r in resp.GetProperty("results").EnumerateArray())
        {
            var data = r.GetProperty("data");
            items.Add(new SearchResult(
                Sku:            Str(data, "variation_id"),
                Name:           Str(r,    "value"),
                Platform:       Str(data, "platform"),
                Condition:      Str(data, "condition"),
                Edition:        Str(data, "edition"),
                Price:          data.TryGetProperty("price", out var pv) && pv.ValueKind == JsonValueKind.Number
                                    ? pv.GetDecimal() : 0m,
                Brand:          Str(data, "brand"),
                Url:            Str(data, "url"),
                ImageUrl:       Str(data, "image_url"),
                VariationCount: r.TryGetProperty("variations", out var vars) &&
                                vars.ValueKind == JsonValueKind.Array
                                    ? vars.GetArrayLength() : 1
            ));
        }

        return (items, total);
    }

    private static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()! : "";

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await Task.CompletedTask;
    }
}
