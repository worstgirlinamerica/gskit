namespace GSKit.Core.Models;

public record InventoryResult(
    string       Sku,
    string       PostalCode,
    double       SearchLat,
    double       SearchLong,
    double       RadiusMiles,
    Store?       PreferredStore,
    List<Store>  StoresWithStock,
    List<Store>  StoresWithoutStock,
    DateTimeOffset FetchedAt
)
{
    public bool AnyInStock => StoresWithStock.Count > 0;
    public int  TotalStores => StoresWithStock.Count + StoresWithoutStock.Count;
}
