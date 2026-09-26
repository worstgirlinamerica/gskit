using System.Text.Json;
using System.Web;
using GSKit.Core.Http;
using GSKit.Core.Models;

namespace GSKit.Core.Scrapers.Inventory;

/// <summary>
/// Queries Stores-FindStores — the live endpoint confirmed from browser recon.
///
/// Endpoint:
///   GET /on/demandware.store/Sites-gamestop-us-Site/default/Stores-FindStores
///
/// Required params:
///   products=<sku>%3a<quantity>   (colon-encoded, e.g. "133857%3a1")
///   lat=<latitude>
///   long=<longitude>
///   radius=<miles>
///   showMap=false
///   source=pdp
///   hasCondition=true
///   hasVariantsAvailableForLookup=true
///   hasVariantsAvailableForPickup=true
///
/// Auth: dwsid session cookie (from SfccSession) — no CSRF needed for GET.
///       cf_clearance needed if CF is in block mode.
/// </summary>
public class StoreInventory
{
    private readonly SfccSession _session;

    public StoreInventory(SfccSession session)
        => _session = session;

    public async Task<InventoryResult> FindStoresAsync(
        string sku,
        double lat,
        double lon,
        double radiusMiles = 100.0,
        int    quantity    = 1,
        CancellationToken ct = default)
    {
        if (!_session.IsSeeded)
            throw new InvalidOperationException(
                "Session not seeded. Call SeedFromChromeAsync() or SeedManual() first.");

        var qs = BuildQueryString(sku, lat, lon, radiusMiles, quantity);
        var res = await _session.SfccGetAsync("Stores-FindStores", qs, ct);

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            // CF block returns HTML — detect it
            if (body.Contains("cf-browser-verification") || body.Contains("Checking your browser"))
                throw new CloudflareBlockException(
                    "Cloudflare blocked the request. " +
                    "Re-run `gskit auth init` to refresh cf_clearance.");

            throw new HttpRequestException(
                $"Stores-FindStores returned {(int)res.StatusCode}. Body: {body[..Math.Min(500, body.Length)]}");
        }

        var json = await res.Content.ReadAsStringAsync(ct);
        return ParseResponse(json, sku, lat, lon, radiusMiles);
    }

    // Overload accepting a zip code — geocodes via nominatim (free, no key needed)
    public async Task<InventoryResult> FindStoresByZipAsync(
        string sku,
        string postalCode,
        double radiusMiles = 100.0,
        CancellationToken ct = default)
    {
        var (lat, lon) = await GeocodingHelper.ZipToLatLonAsync(postalCode, ct);
        return await FindStoresAsync(sku, lat, lon, radiusMiles, ct: ct);
    }

    // ── Query string builder ──────────────────────────────────────────────────
    private static string BuildQueryString(
        string sku, double lat, double lon, double radius, int qty)
    {
        // products param uses colon-encoded SKU:qty
        var products = HttpUtility.UrlEncode($"{sku}:{qty}"); // → "133857%3a1"

        return string.Join("&",
            "hasCondition=true",
            "hasVariantsAvailableForLookup=true",
            "hasVariantsAvailableForPickup=true",
            "source=pdp",
            "showMap=false",
            $"products={products}",
            "selectedStore=undefined",
            $"lat={lat}",
            $"long={lon}",
            $"radius={radius:F1}"
        );
    }

    // ── Response parser — mapped from live field names ────────────────────────
    private static InventoryResult ParseResponse(
        string json, string sku, double lat, double lon, double radius)
    {
        using var doc  = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var preferredStore = root.TryGetProperty("preferredStore", out var ps) && ps.ValueKind != JsonValueKind.Null
            ? ParseStore(ps)
            : null;

        var allStores = root.TryGetProperty("stores", out var storesEl)
            ? storesEl.EnumerateArray().Select(ParseStore).ToList()
            : [];

        var inStock  = allStores.Where(s => s.IsInStock).ToList();
        var outStock = allStores.Where(s => !s.IsInStock).ToList();

        // Preferred store is separate in the response — add to appropriate bucket
        if (preferredStore != null && allStores.All(s => s.Id != preferredStore.Id))
        {
            if (preferredStore.IsInStock) inStock.Insert(0, preferredStore);
            else outStock.Insert(0, preferredStore);
        }

        return new InventoryResult(
            Sku:                sku,
            PostalCode:         "",
            SearchLat:          lat,
            SearchLong:         lon,
            RadiusMiles:        radius,
            PreferredStore:     preferredStore,
            StoresWithStock:    inStock,
            StoresWithoutStock: outStock,
            FetchedAt:          DateTimeOffset.UtcNow
        );
    }

    private static Store ParseStore(JsonElement s)
    {
        // Hours — two formats in the response:
        // 1. storeOperationHours: JSON string with 24hr times (preferredStore only)
        // 2. hours: array of {day, hours} with display strings (other stores)
        var displayHours = new List<StoreHoursDisplay>();
        if (s.TryGetProperty("hours", out var hoursArr) &&
            hoursArr.ValueKind == JsonValueKind.Array)
        {
            displayHours = hoursArr.EnumerateArray()
                .Select(h => new StoreHoursDisplay(
                    h.GetProperty("day").GetString()!,
                    h.GetProperty("hours").GetString()!
                ))
                .ToList();
        }

        // Conditions in stock
        var conditionsInStock = new List<ConditionStock>();
        if (s.TryGetProperty("conditionsEligibleForPickup", out var conds) &&
            conds.ValueKind == JsonValueKind.Array)
        {
            conditionsInStock = conds.EnumerateArray()
                .Select(c => new ConditionStock(
                    c.GetProperty("condition").GetString()!,
                    c.GetProperty("pid").GetString()!,
                    c.GetProperty("isInStock").GetBoolean(),
                    c.GetProperty("displayName").GetString()!
                ))
                .ToList();
        }

        // Raw inventory counts (only present on preferredStore)
        var inventory = new List<SkuInventory>();
        if (s.TryGetProperty("inventory", out var invArr) &&
            invArr.ValueKind == JsonValueKind.Array)
        {
            inventory = invArr.EnumerateArray()
                .Select(i => new SkuInventory(
                    i.GetProperty("sku").GetString()!,
                    i.TryGetProperty("count", out var cnt) ? cnt.GetInt32() : 0
                ))
                .ToList();
        }

        // Pickup details
        var pickup = new StorePickupDetails(false, false, false, false);
        if (s.TryGetProperty("storePickupDetails", out var pd))
        {
            pickup = new StorePickupDetails(
                HopsEnabled:      pd.GetProperty("hopsEnabled").GetBoolean(),
                BopsEnabled:      pd.GetProperty("bopsEnabled").GetBoolean(),
                IspuEnabled:      pd.GetProperty("ispuEnabled").GetBoolean(),
                IsOnMilitaryBase: pd.GetProperty("isOnMilitaryBase").GetBoolean()
            );
        }

        return new Store(
            Id:               s.GetProperty("ID").GetString()!,
            Name:             s.GetProperty("name").GetString()!.Trim(),
            Address1:         s.GetProperty("address1").GetString()!,
            Address2:         s.TryGetProperty("address2", out var a2) && a2.ValueKind != JsonValueKind.Null
                                  ? a2.GetString() : null,
            City:             s.GetProperty("city").GetString()!,
            State:            s.GetProperty("stateCode").GetString()!,
            PostalCode:       s.GetProperty("postalCode").GetString()!,
            Phone:            s.TryGetProperty("phone", out var ph) ? ph.GetString() : null,
            Latitude:         s.GetProperty("latitude").GetDouble(),
            Longitude:        s.GetProperty("longitude").GetDouble(),
            DistanceMiles:    double.TryParse(s.GetProperty("distance").GetString(), out var d) ? d : 0,
            IsInStock:        s.GetProperty("isInStock").GetBoolean(),
            IsLimitedStock:   s.GetProperty("isLimitedStock").GetBoolean(),
            IsPreferredStore: s.TryGetProperty("isPreferredStore", out var ip) && ip.GetBoolean(),
            IsCurrentlyOpen:  s.TryGetProperty("isCurrentlyOpen", out var ico) && ico.GetBoolean(),
            TodayClosingTime: s.TryGetProperty("todayClosingTime", out var tc) ? tc.GetString() : null,
            StoreMode:        s.GetProperty("storeMode").GetString()!,
            PickupDetails:    pickup,
            ConditionsInStock: conditionsInStock,
            Hours:            displayHours,
            Inventory:        inventory
        );
    }
}

public class CloudflareBlockException(string message) : Exception(message);
