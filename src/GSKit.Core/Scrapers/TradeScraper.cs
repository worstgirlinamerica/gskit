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
        // Trade-GetSuggestions always returns an HTML partial — no JSON, no cookies needed.
        // Confirmed from live response: <div class="suggestions"><div class="container">...
        // Structure: .product-item elements, each with data-pid + .product-name + img
        var qs  = $"q={HttpUtility.UrlEncode(query)}&format=ajax";
        var res = await client.SfccGetAsync(
            "Trade-GetSuggestions", qs,
            refererSku: null, ct: ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Trade-GetSuggestions returned {(int)res.StatusCode}: {body[..Math.Min(200, body.Length)]}");

        // CF block check — if we ever start getting blocked
        if (body.Contains("cf-error-details") || body.Contains("Attention Required"))
            throw new CloudflareBlockException("Trade-GetSuggestions is CF-blocked.");

        var parser  = new HtmlParser();
        using var doc = parser.ParseDocument(body);
        var results = new List<TradeSuggestion>();

        // Each result is a .product-item (or .item) with data-pid and a .product-name span/div
        // Confirmed HTML structure from live response:
        //   <div class="product-item" data-pid="...">
        //     <img src="...">
        //     <div class="product-name">...</div>
        //   </div>
        foreach (var item in doc.QuerySelectorAll(".product-item, .item.product"))
        {
            var pid  = item.GetAttribute("data-pid") ?? item.GetAttribute("data-product-id") ?? "";
            var name = item.QuerySelector(".product-name, .name, a")?.TextContent.Trim() ?? "";
            var img  = item.QuerySelector("img")?.GetAttribute("src") ?? "";

            // Skip header/label rows that have no pid
            if (pid.Length == 0 && name.Length == 0) continue;

            results.Add(new TradeSuggestion(
                ProductId: pid,
                Name:      name,
                ImageUrl:  img
            ));
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
            refererSku: null, ct: ct);

        // Trade-Show uses text/html Accept — override for this call
        var body = await res.Content.ReadAsStringAsync(ct);

        var trimmedShow = body.TrimStart();
        if (trimmedShow.StartsWith('<') && (body.Contains("cf-error-details") || body.Contains("Attention Required") ||
            body.Contains("cf-browser-verification") || body.Contains("enable_cookies")))
            throw new CloudflareBlockException(
                $"Trade-Show is CF-blocked (needs Chrome cookies). Open gamestop.com/trade/ in Chrome first.\n" +
                $"Response: {body[..Math.Min(300, body.Length)]}");

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

}
