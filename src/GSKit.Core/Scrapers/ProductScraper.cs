using System.Text.Json;
using System.Web;
using GSKit.Core.Http;
using GSKit.Core.Models;

namespace GSKit.Core.Scrapers;

/// <summary>
/// Scrapes product-level endpoints discovered in HAR analysis (2026-09-27):
///   • Product-Variation  → title, price, availability, trade-in
///   • Product-SameDayDelivery → SDD eligibility
/// </summary>
public sealed class ProductScraper(GameStopClient client)
{
    // ── Product-Variation ─────────────────────────────────────────────────────

    public async Task<ProductInfo?> GetProductInfoAsync(
        string sku,
        string condition  = "Pre-Owned",
        string platform   = "",
        string edition    = "Standard",
        CancellationToken ct = default)
    {
        // Query string mirrors what the PDP fires in the HAR
        var qs = string.Join("&",
            $"dwvar_{HttpUtility.UrlEncode(sku)}_condition={HttpUtility.UrlEncode(condition)}",
            $"dwvar_{HttpUtility.UrlEncode(sku)}_edition={HttpUtility.UrlEncode(edition)}",
            platform.Length > 0 ? $"dwvar_{HttpUtility.UrlEncode(sku)}_platform={HttpUtility.UrlEncode(platform)}" : "",
            $"pid={HttpUtility.UrlEncode(sku)}",
            "quantity=1",
            "rt=productDetailsRedesign"
        ).TrimStart('&').Replace("&&", "&");

        var res  = await client.SfccGetAsync("Product-Variation", qs, refererSku: sku, ct: ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("product", out var p)) return null;

        var price      = p.TryGetProperty("price", out var pr)          ? pr : default;
        var avail      = p.TryGetProperty("availability", out var av)   ? av : default;
        var salesPrice = price.ValueKind != JsonValueKind.Undefined
            ? price.TryGetProperty("sales", out var sp) ? sp : default
            : default;

        decimal tradeBase = 0;
        if (p.TryGetProperty("tradeBasePrice", out var tbp) &&
            tbp.ValueKind == JsonValueKind.Number)
            tradeBase = tbp.GetDecimal();

        string availMsg = "Unknown";
        if (avail.ValueKind == JsonValueKind.Object &&
            avail.TryGetProperty("messages", out var msgs) &&
            msgs.ValueKind == JsonValueKind.Array)
        {
            var msgList = msgs.EnumerateArray()
                              .Select(m => m.GetString() ?? "")
                              .Where(m => m.Length > 0)
                              .ToList();
            if (msgList.Count > 0) availMsg = string.Join("; ", msgList);
        }

        return new ProductInfo
        {
            Sku               = sku,
            Title             = Str(p, "productName"),
            Condition         = Str(p, "productCondition"),
            Platform          = Str(p, "productPlatform"),
            PriceFormatted    = salesPrice.ValueKind != JsonValueKind.Undefined
                                    ? Str(salesPrice, "formatted") : "—",
            PriceValue        = salesPrice.ValueKind != JsonValueKind.Undefined &&
                                salesPrice.TryGetProperty("value", out var pv) &&
                                pv.ValueKind == JsonValueKind.Number
                                    ? pv.GetDecimal() : 0m,
            ProPriceFormatted = price.ValueKind != JsonValueKind.Undefined &&
                                price.TryGetProperty("salePriceIncludingPro", out var pp) &&
                                pp.ValueKind == JsonValueKind.Object
                                    ? Str(pp, "formatted") : null,
            Available         = Bool(p, "available"),
            ReadyToOrder      = Bool(p, "readyToOrder"),
            AvailabilityMessage = availMsg,
            IsPreorder        = Bool(p, "preorder"),
            IsBackorder       = Bool(p, "backorder"),
            IsTradeable       = Bool(p, "isTradeable"),
            TradeBasePrice    = tradeBase,
            Publisher         = NullableStr(p, "publisher"),
            Developer         = NullableStr(p, "developer"),
            Genre             = NullableStr(p, "webGenre"),
            ReleaseDate       = NullableStr(p, "releaseDate"),
        };
    }

    // ── Product-SameDayDelivery ───────────────────────────────────────────────

    public async Task<SameDayResult?> GetSameDayDeliveryAsync(
        string sku,
        CancellationToken ct = default)
    {
        var res  = await client.SfccGetAsync(
            "Product-SameDayDelivery", $"pid={HttpUtility.UrlEncode(sku)}",
            refererSku: sku, ct: ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;

        return new SameDayResult
        {
            HideSdd       = Bool(r, "hideSDD"),
            NearBy        = Bool(r, "nearBy"),
            AtcDisable    = Bool(r, "atcDisable"),
            ProductAvail  = Bool(r, "isProductAvailable"),
            IsTimeCutOff  = Bool(r, "isTimeCutOff"),
            TimeLeft      = Str(r, "timeLeft"),
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()! : "";

    private static string? NullableStr(JsonElement el, string key)
    {
        var s = Str(el, key);
        return s.Length > 0 ? s : null;
    }

    private static bool Bool(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
}
