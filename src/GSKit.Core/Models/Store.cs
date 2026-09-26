using System.Text.Json;
using System.Text.Json.Serialization;

namespace GSKit.Core.Models;

public record StoreHours(string Day, string Open, string Close);
public record StoreHoursDisplay(string Day, string Hours);

public record ConditionStock(
    string Condition,
    string Pid,
    bool IsInStock,
    string DisplayName
);

public record StorePickupDetails(
    bool HopsEnabled,   // ship-to-store
    bool BopsEnabled,   // buy online pick up in store
    bool IspuEnabled,   // in-store pickup
    bool IsOnMilitaryBase
);

public record Store(
    string   Id,
    string   Name,
    string   Address1,
    string?  Address2,
    string   City,
    string   State,
    string   PostalCode,
    string?  Phone,
    double   Latitude,
    double   Longitude,
    double   DistanceMiles,
    bool     IsInStock,
    bool     IsLimitedStock,
    bool     IsPreferredStore,
    bool     IsCurrentlyOpen,
    string?  TodayClosingTime,
    string   StoreMode,               // "ACTIVE" etc
    StorePickupDetails PickupDetails,
    List<ConditionStock> ConditionsInStock,
    List<StoreHoursDisplay> Hours,
    // raw inventory array — may be empty even when isInStock=true
    // GameStop only populates count[] on the preferredStore
    List<SkuInventory> Inventory
);

public record SkuInventory(string Sku, int Count);
