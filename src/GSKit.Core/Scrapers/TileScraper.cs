using System.Text.Json;
using System.Web;
using GSKit.Core.Http;
using GSKit.Core.Models;

namespace GSKit.Core.Scrapers;

/// <summary>
/// Scrapes Tile-GetProductsJSON — lightweight batch price + availability lookup.
///
/// From HAR (2026-09-27): the PDP fires this to populate "More Like This" carousels.
/// Accepts a comma-separated list of SKUs in the `data` param.
/// Returns name, price (base/sale/pro), availability flags (BOPS, SDD), image URL.
///
/// NOTE: 403s from datacenter IPs (CF). Residential only — same restriction as FindStores.
/// </summary>
public sealed class TileScraper(GameStopClient client)
{
    public async Task<List<TileProduct>> GetTilesAsync(
        IEnumerable<string> skus,
        CancellationToken   ct = default)
    {
        var skuList = string.Join(",", skus.Select(HttpUtility.UrlEncode));
        var qs      = $"data={skuList}&useTileImage=false";

        var res  = await client.SfccGetAsync("Tile-GetProductsJSON", qs, refererSku: null, ct: ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            if (body.Contains("cf-browser-verification") || body.Contains("Checking your browser"))
                throw new CloudflareBlockException(
                    "Cloudflare blocked this request. Tile-GetProductsJSON requires a residential IP.");
            throw new HttpRequestException($"Tile-GetProductsJSON returned {(int)res.StatusCode}");
        }

        using var doc   = JsonDocument.Parse(body);
        var       root  = doc.RootElement;
        var       tiles = new List<TileProduct>();

        if (!root.TryGetProperty("productsJSON", out var prods) ||
            prods.ValueKind != JsonValueKind.Object)
            return tiles;

        foreach (var entry in prods.EnumerateObject())
        {
            var p     = entry.Value;
            var price = p.TryGetProperty("price", out var pr) ? pr : default;
            var avail = p.TryGetProperty("availability", out var av) ? av : default;

            tiles.Add(new TileProduct(
                Sku:          Str(p, "id"),
                Name:         Str(p, "name"),
                PriceBase:    Dec(price, "base"),
                PriceSale:    Dec(price, "sale"),
                PricePro:     Dec(price, "pro"),
                Available:    Bool(avail, "available"),
                ReadyToOrder: Bool(avail, "readyToOrder"),
                AllowBOPS:    Bool(avail, "allowBOPS"),
                AllowSDD:     Bool(avail, "allowSDD"),
                IsDigital:    Bool(avail, "isDigitalProduct"),
                Url:          p.TryGetProperty("image", out var img) ? Str(img, "base") : "",
                ImageUrl:     p.TryGetProperty("image", out var img2) ? Str(img2, "base") : ""
            ));
        }

        return tiles;
    }

    private static string   Str(JsonElement el, string k) =>
        el.ValueKind != JsonValueKind.Undefined &&
        el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()! : "";

    private static decimal? Dec(JsonElement el, string k)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return null;
        if (!el.TryGetProperty(k, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
        if (v.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.GetString(), out var d)) return d;
        return null;
    }

    private static bool Bool(JsonElement el, string k) =>
        el.ValueKind != JsonValueKind.Undefined &&
        el.TryGetProperty(k, out var v) &&
        v.ValueKind == JsonValueKind.True;
}
