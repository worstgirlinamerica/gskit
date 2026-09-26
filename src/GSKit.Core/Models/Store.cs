namespace GSKit.Core.Models;

public record StoreHoursDisplay(string Day, string Hours);

public record ConditionStock(
    string Condition,
    string Pid,
    bool   IsInStock,
    string DisplayName
);

public record StorePickupDetails(
    bool HopsEnabled,       // ship-to-store
    bool BopsEnabled,       // buy online pick up in store
    bool IspuEnabled,       // in-store pickup
    bool IsOnMilitaryBase
);

public record Store(
    string   Id,
    string   Name,
    string   Address1,
    string?  Address2,
    string   City,
    string   StateCode,             // matches API field stateCode
    string   PostalCode,
    string?  Phone,
    double   Latitude,
    double   Longitude,
    double   DistanceMiles,
    bool     IsInStock,
    bool     IsLimitedStock,
    bool     IsPreferredStore,
    bool?    IsCurrentlyOpen,       // nullable — not present on all stores
    string?  TodayClosingTime,
    string   StoreMode,
    StorePickupDetails?             PickupDetails,
    List<ConditionStock>            ConditionsInStock,
    List<StoreHoursDisplay>         Hours,
    List<SkuInventory>              Inventory
)
{
    /// <summary>Convenience — eligible conditions regardless of stock status</summary>
    public IEnumerable<ConditionStock> ConditionsEligibleForPickup => ConditionsInStock;
}

public record SkuInventory(string Sku, int Count);
