using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using AngleSharp.Html.Parser;
using GSKit.Core.Http;
using GSKit.Core.Models;

namespace GSKit.Core.Scrapers;

/// <summary>
/// Scrapes GameStop's trade-in wizard endpoints:
///
///   Trade-GetSuggestions?q=&lt;query&gt;
///     Autocomplete search used by the trade wizard. Returns a list of products
///     matching the query — each has a productId (their internal ID, not the SKU
///     used elsewhere), display name, and image. Use the productId to call Trade-Show.
///
///   Trade-Show?pid=&lt;productId&gt;&amp;condition=&lt;condition&gt;
///     Server-rendered HTML partial. Returns the trade value breakdown for a given
///     product + condition: cash value, in-store credit value, and pro bonus.
///     Parses the rendered HTML to extract dollar amounts.
///
/// Both endpoints are SFCC controllers, CF-blocked from datacenter IPs.
/// Residential only (same restriction as Tile-GetProductsJSON).
///
/// NOTE: The productId used here is NOT the same as the SKU used in other commands.
///   SKU 133857 (Infinite Warfare Xbox One) has a different productId in the trade system.
///   Use trade-search to find the correct productId, then trade-value to get values.
/// </summary>
public sealed class TradeScraper(GameStopClient client)
{
    // ── Trade-GetSuggestions ──────────────────────────────────────────────────

    public async Task<List<TradeSuggestion>> GetSuggestionsAsync(
        string            query,
        CancellationToken ct = default)
    {
        var qs  = $"q={HttpUtility.UrlEncode(query)}&format=ajax";
        var res = await client.SfccGetAsync(
            "Trade-GetSuggestions", qs,
            refererSku: null, withCookies: true, ct: ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        // CF sometimes returns block pages with 200 OK — check body before status
        if (body.Contains("Attention Required") || body.Contains("cf-error-details") ||
            body.Contains("cf-browser-verification") || body.Contains("Checking your browser"))
            throw new CloudflareBlockException(
                "Trade-GetSuggestions is CF-blocked. This endpoint requires cookies from a live browser session — run gskit from your Mac with Chrome cookies available, or capture the response in a HAR first.");

        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Trade-GetSuggestions returned {(int)res.StatusCode}: {body[..Math.Min(200, body.Length)]}");

        // Response is JSON: { "suggestions": [ { "productId": "...", "name": "...", "image": "..." }, ... ] }
        // Some older responses use "products" instead of "suggestions" — try both.
        using var doc  = JsonDocument.Parse(body);
        var       root = doc.RootElement;

        var results = new List<TradeSuggestion>();
        JsonElement arr = default;

        if (!root.TryGetProperty("suggestions", out arr) || arr.ValueKind != JsonValueKind.Array)
            root.TryGetProperty("products",    out arr);

        if (arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                results.Add(new TradeSuggestion(
                    ProductId: Str(item, "productId"),
                    Name:      Str(item, "name"),
                    ImageUrl:  Str(item, "image")
                ));
            }
        }

        return results;
    }

    // ── Trade-Show ────────────────────────────────────────────────────────────

    public async Task<TradeValue?> GetTradeValueAsync(
        string            productId,
        string            condition  = "Pre-Owned",
        CancellationToken ct         = default)
    {
        // Trade-Show renders a full HTML partial, not JSON.
        // Params from HAR: pid=<productId>&condition=<condition>&format=ajax
        var qs  = $"pid={HttpUtility.UrlEncode(productId)}&condition={HttpUtility.UrlEncode(condition)}&format=ajax";
        var res = await client.SfccGetAsync(
            "Trade-Show", qs,
            refererSku: null, withCookies: true, ct: ct);

        // Trade-Show uses text/html Accept — override for this call
        var body = await res.Content.ReadAsStringAsync(ct);

        if (body.Contains("Attention Required") || body.Contains("cf-error-details") ||
            body.Contains("cf-browser-verification") || body.Contains("Checking your browser"))
            throw new CloudflareBlockException(
                "Trade-Show is CF-blocked. Needs cookies from a live browser session.");

        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Trade-Show returned {(int)res.StatusCode}: {body[..Math.Min(200, body.Length)]}");

        return ParseTradeHtml(body, productId, condition);
    }

    // ── HTML parser ───────────────────────────────────────────────────────────

    private static TradeValue ParseTradeHtml(string html, string productId, string condition)
    {
        var parser = new HtmlParser();
        using var doc = parser.ParseDocument(html);

        // GS trade HTML structure (from Wayback + known SFCC trade patterns):
        //   .trade-value-cash   or data-type="cash"    → cash payout
        //   .trade-value-credit or data-type="credit"  → in-store credit
        //   .trade-value-pro    or data-type="pro"     → Pro member bonus
        //   .trade-product-name                        → product display name

        static decimal ParsePrice(string? s)
        {
            if (s is null) return 0m;
            var cleaned = Regex.Replace(s, @"[^\d\.]", "");
            return decimal.TryParse(cleaned, out var d) ? d : 0m;
        }

        // Try data-attribute selectors first (newer widget), fall back to class names
        string cashText   = doc.QuerySelector("[data-type='cash'] .trade-value, .trade-value-cash, .cash-value")
                               ?.TextContent.Trim() ?? "";
        string creditText = doc.QuerySelector("[data-type='credit'] .trade-value, .trade-value-credit, .credit-value")
                               ?.TextContent.Trim() ?? "";
        string proText    = doc.QuerySelector("[data-type='pro'] .trade-value, .trade-value-pro, .pro-value")
                               ?.TextContent.Trim() ?? "";
        string name       = doc.QuerySelector(".trade-product-name, .product-name, h1.name")
                               ?.TextContent.Trim() ?? "";

        // Fallback: scan all dollar amounts in the page and label by position/context
        if (cashText.Length == 0 && creditText.Length == 0)
        {
            var allValues = doc.QuerySelectorAll(".trade-value, .value, [class*='trade'][class*='value']");
            var amounts   = allValues.Select(el => el.TextContent.Trim()).Where(t => t.StartsWith('$')).ToList();
            // Typical order in GS widget: credit first, cash second, pro third
            if (amounts.Count >= 1) creditText = amounts[0];
            if (amounts.Count >= 2) cashText   = amounts[1];
            if (amounts.Count >= 3) proText     = amounts[2];
        }

        return new TradeValue(
            ProductId:        productId,
            Condition:        condition,
            ProductName:      name,
            CashValue:        ParsePrice(cashText.Length > 0 ? cashText : null),
            CreditValue:      ParsePrice(creditText.Length > 0 ? creditText : null),
            ProBonusValue:    ParsePrice(proText.Length > 0 ? proText : null),
            RawHtml:          html   // keep raw for --debug
        );
    }

    private static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()! : "";
}
