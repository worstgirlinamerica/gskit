using System.Text.Json;
using System.Web;
using GSKit.Core.Http;
using GSKit.Core.Models;

namespace GSKit.Core.Scrapers;

/// <summary>
/// Scrapes Stores-ProductDetailStoreAvailability — per-variant breakdown at the
/// session's preferred store.
///
/// From HAR (2026-09-27): fired by the PDP on page load with ?pid=&lt;sku&gt;&redesignFlag=false.
/// Returns one entry per variant (condition × platform × edition) with individual
/// inStock / inStockCount and nearestStoreDetailsObj showing which store has it.
///
/// Use this when you want to know exactly which platform/condition combo is available
/// at your preferred store, rather than searching a radius.
///
/// NOTE: 403s from datacenter IPs. Residential only.
/// </summary>
public sealed class StoreAvailabilityScraper(GameStopClient client)
{
    public async Task<ProductDetailStoreAvailability?> GetAsync(
        string            sku,
        CancellationToken ct = default)
    {
        var qs  = $"pid={HttpUtility.UrlEncode(sku)}&redesignFlag=false";
        var res = await client.SfccGetAsync(
            "Stores-ProductDetailStoreAvailability", qs, refererSku: sku, ct: ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            if (body.Contains("cf-browser-verification") || body.Contains("Checking your browser"))
                throw new CloudflareBlockException(
                    "Cloudflare blocked this request. Stores-ProductDetailStoreAvailability requires a residential IP.");
            throw new HttpRequestException(
                $"Stores-ProductDetailStoreAvailability returned {(int)res.StatusCode}");
        }

        using var doc  = JsonDocument.Parse(body);
        var       root = doc.RootElement;

        var variants = new List<VariantAvailability>();

        if (root.TryGetProperty("products", out var prods) &&
            prods.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in prods.EnumerateArray())
            {
                var attrs = p.TryGetProperty("variationAttributes", out var va) ? va : default;

                string nearestId = "", nearestName = "";
                int    nearestCount = 0;
                if (p.TryGetProperty("nearestStoreDetailsObj", out var ns) &&
                    ns.ValueKind == JsonValueKind.Object)
                {
                    nearestId    = Str(ns, "nearestStoreId");
                    nearestName  = Str(ns, "storeName");
                    nearestCount = Int(ns, "inventoryAvailable");
                }

                variants.Add(new VariantAvailability(
                    Sku:               Str(p, "id"),
                    Condition:         attrs.ValueKind != JsonValueKind.Undefined ? Str(attrs, "condition") : "",
                    Platform:          attrs.ValueKind != JsonValueKind.Undefined ? Str(attrs, "platform")  : "",
                    Edition:           attrs.ValueKind != JsonValueKind.Undefined ? Str(attrs, "edition")   : "",
                    InStock:           Bool(p, "inStock"),
                    InStockCount:      Int(p,  "inStockCount"),
                    AllowBOPS:         Bool(p, "allowBOPS"),
                    NearestStoreId:    nearestId,
                    NearestStoreName:  nearestName,
                    NearestStoreCount: nearestCount
                ));
            }
        }

        return new ProductDetailStoreAvailability(
            Sku:                           sku,
            StoreName:                     Str(root, "storeName"),
            StoreDetails:                  Str(root, "storeDetails"),
            HasVariantsAvailableForPickup: Bool(root, "hasVariantsAvailableForPickup"),
            HasVariantsInStock:            Bool(root, "hasVariantsAvailableForPickupInStock"),
            Variants:                      variants
        );
    }

    private static string Str(JsonElement el, string k) =>
        el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()! : "";
    private static bool   Bool(JsonElement el, string k) =>
        el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
    private static int    Int(JsonElement el, string k) =>
        el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : 0;
}
