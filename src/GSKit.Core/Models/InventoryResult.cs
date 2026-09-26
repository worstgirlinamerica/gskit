namespace GSKit.Core.Models;

public sealed class InventoryResult
{
    public string   Sku         { get; init; } = "";
    public double   QueryLat    { get; init; }
    public double   QueryLon    { get; init; }
    public double   RadiusMiles { get; init; }
    public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.UtcNow;
    public string?  RawJson     { get; init; }
    public int      HttpStatus  { get; init; }

    /// <summary>
    /// ALL stores returned by the API, in distance order.
    /// Includes in-stock AND out-of-stock. Never filtered.
    /// The CLI decides what to show — we never drop cities here.
    /// </summary>
    public IReadOnlyList<Store> Stores { get; init; } = [];

    public IEnumerable<Store> InStock    => Stores.Where(s => s.IsInStock);
    public IEnumerable<Store> OutOfStock => Stores.Where(s => !s.IsInStock);
    public int InStockCount  => Stores.Count(s => s.IsInStock);
    public int TotalStores   => Stores.Count;
}
