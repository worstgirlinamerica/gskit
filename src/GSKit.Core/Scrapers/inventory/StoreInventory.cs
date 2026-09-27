using System.Text.Json;
using System.Web;
using GSKit.Core.Http;
using GSKit.Core.Models;

namespace GSKit.Core.Scrapers.Inventory;

public sealed class StoreInventory(GameStopClient client)
{
    public async Task<InventoryResult> FindStoresAsync(
        string sku,
        double lat,
        double lon,
        double radiusMiles    = 100.0,
        int    quantity       = 1,
        bool   captureRawJson = false,
        CancellationToken ct  = default)
    {
        var qs  = BuildQueryString(sku, lat, lon, radiusMiles, quantity);
        var res = await client.SfccGetAsync("Stores-FindStores", qs, refererSku: sku, ct: ct);

        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            if (body.Contains("cf-browser-verification") || body.Contains("Checking your browser"))
                throw new CloudflareBlockException(
                    "Cloudflare is challenging this IP. " +
                    "This usually means you're on a VPN or datacenter IP. " +
                    "Try from a residential connection, or pass --session with cookies from your browser.");

            throw new HttpRequestException(
                $"Stores-FindStores returned {(int)res.StatusCode}. " +
                $"Body: {body[..Math.Min(400, body.Length)]}");
        }

        return ParseResponse(body, sku, lat, lon, radiusMiles, captureRawJson);
    }

    public async Task<InventoryResult> FindStoresByZipAsync(
        string sku,
        string postalCode,
        double radiusMiles    = 100.0,
        bool   captureRawJson = false,
        CancellationToken ct  = default)
    {
        var (lat, lon) = await GeocodingHelper.ZipToLatLonAsync(postalCode, ct);
        return await FindStoresAsync(sku, lat, lon, radiusMiles,
                                     captureRawJson: captureRawJson, ct: ct);
    }

    private static string BuildQueryString(
        string sku, double lat, double lon, double radius, int qty)
    {
        var products = HttpUtility.UrlEncode($"{sku}:{qty}");
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

    private static InventoryResult ParseResponse(
        string json, string sku, double lat, double lon,
        double radius, bool captureRawJson)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Store? preferredStore = null;
        string? preferredId   = null;

        if (root.TryGetProperty("preferredStore", out var ps) &&
            ps.ValueKind == JsonValueKind.Object)
        {
            preferredStore = ParseStore(ps);
            preferredId    = preferredStore.Id;
        }

        var stores = new List<Store>();
        if (root.TryGetProperty("stores", out var storesEl) &&
            storesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in storesEl.EnumerateArray())
                stores.Add(ParseStore(el));
        }

        // preferredStore is returned separately by the API — merge it in at correct distance position
        if (preferredStore != null && stores.All(s => s.Id != preferredId))
        {
            stores.Add(preferredStore);
            stores.Sort((a, b) => a.DistanceMiles.CompareTo(b.DistanceMiles));
        }

        return new InventoryResult
        {
            Sku         = sku,
            QueryLat    = lat,
            QueryLon    = lon,
            RadiusMiles = radius,
            FetchedAt   = DateTimeOffset.UtcNow,
            RawJson     = captureRawJson ? json : null,
            Stores      = stores,
        };
    }

    private static Store ParseStore(JsonElement s)
    {
        var hours = new List<StoreHoursDisplay>();
        if (s.TryGetProperty("hours", out var ha) && ha.ValueKind == JsonValueKind.Array)
            foreach (var h in ha.EnumerateArray())
                hours.Add(new StoreHoursDisplay(
                    h.GetProperty("day").GetString()!,
                    h.GetProperty("hours").GetString()!));

        var conditions = new List<ConditionStock>();
        if (s.TryGetProperty("conditionsEligibleForPickup", out var ca) &&
            ca.ValueKind == JsonValueKind.Array)
            foreach (var c in ca.EnumerateArray())
                conditions.Add(new ConditionStock(
                    c.GetProperty("condition").GetString()!,
                    c.GetProperty("pid").GetString()!,
                    c.GetProperty("isInStock").GetBoolean(),
                    c.GetProperty("displayName").GetString()!));

        var inventory = new List<SkuInventory>();
        if (s.TryGetProperty("inventory", out var ia) && ia.ValueKind == JsonValueKind.Array)
            foreach (var i in ia.EnumerateArray())
                inventory.Add(new SkuInventory(
                    i.GetProperty("sku").GetString()!,
                    i.TryGetProperty("count", out var cnt) ? cnt.GetInt32() : 0));

        StorePickupDetails? pickup = null;
        if (s.TryGetProperty("storePickupDetails", out var pd) &&
            pd.ValueKind == JsonValueKind.Object)
            pickup = new StorePickupDetails(
                Bool(pd, "hopsEnabled"), Bool(pd, "bopsEnabled"),
                Bool(pd, "ispuEnabled"), Bool(pd, "isOnMilitaryBase"));

        double dist = 0;
        if (s.TryGetProperty("distance", out var de))
        {
            if (de.ValueKind == JsonValueKind.String) double.TryParse(de.GetString(), out dist);
            else if (de.ValueKind == JsonValueKind.Number) dist = de.GetDouble();
        }

        bool? isOpen = null;
        if (s.TryGetProperty("isCurrentlyOpen", out var ico) &&
            (ico.ValueKind == JsonValueKind.True || ico.ValueKind == JsonValueKind.False))
            isOpen = ico.GetBoolean();

        return new Store(
            Id:               s.GetProperty("ID").GetString()!,
            Name:             s.GetProperty("name").GetString()!.Trim(),
            Address1:         s.GetProperty("address1").GetString()!,
            Address2:         s.TryGetProperty("address2", out var a2) &&
                              a2.ValueKind != JsonValueKind.Null ? a2.GetString() : null,
            City:             s.GetProperty("city").GetString()!,
            StateCode:        s.GetProperty("stateCode").GetString()!,
            PostalCode:       s.GetProperty("postalCode").GetString()!,
            Phone:            s.TryGetProperty("phone", out var ph) &&
                              ph.ValueKind == JsonValueKind.String ? ph.GetString() : null,
            Latitude:         s.GetProperty("latitude").GetDouble(),
            Longitude:        s.GetProperty("longitude").GetDouble(),
            DistanceMiles:    dist,
            IsInStock:        Bool(s, "isInStock"),
            IsLimitedStock:   Bool(s, "isLimitedStock"),
            IsPreferredStore: Bool(s, "isPreferredStore"),
            IsCurrentlyOpen:  isOpen,
            TodayClosingTime: s.TryGetProperty("todayClosingTime", out var tc) &&
                              tc.ValueKind == JsonValueKind.String ? tc.GetString() : null,
            StoreMode:        s.TryGetProperty("storeMode", out var sm) &&
                              sm.ValueKind == JsonValueKind.String ? sm.GetString()! : "ACTIVE",
            PickupDetails:    pickup,
            ConditionsInStock: conditions,
            Hours:            hours,
            Inventory:        inventory);
    }

    private static bool Bool(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;
}
