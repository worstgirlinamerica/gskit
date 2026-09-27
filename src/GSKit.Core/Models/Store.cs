namespace GSKit.Core.Models;

/// <summary>
/// One day's hours from storeOperationHours — open/close are "HHMM" 24h strings e.g. "1000", "2100".
/// </summary>
public record StoreHours(string Day, string Open, string Close)
{
    /// <summary>Human-readable like "10:00 AM – 9:00 PM"</summary>
    public string Display => $"{Fmt(Open)} – {Fmt(Close)}";

    private static string Fmt(string hhmm)
    {
        if (hhmm.Length != 4) return hhmm;
        int h = int.Parse(hhmm[..2]);
        int m = int.Parse(hhmm[2..]);
        var ampm = h >= 12 ? "PM" : "AM";
        int h12 = h % 12 == 0 ? 12 : h % 12;
        return m == 0 ? $"{h12} {ampm}" : $"{h12}:{m:D2} {ampm}";
    }
}

public record ConditionStock(
    string Condition,
    string Pid,
    bool   IsInStock,
    string DisplayName
);

public record StorePickupDetails(
    bool HopsEnabled,
    bool BopsEnabled,
    bool IspuEnabled,
    bool IsOnMilitaryBase
);

public record Store(
    string   Id,
    string   Name,
    string   Address1,
    string?  Address2,
    string   City,
    string   StateCode,
    string   PostalCode,
    string?  Phone,
    double   Latitude,
    double   Longitude,
    double   DistanceMiles,
    bool     IsInStock,
    bool     IsLimitedStock,
    bool     IsPreferredStore,
    string   StoreMode,
    StorePickupDetails?        PickupDetails,
    List<ConditionStock>       ConditionsEligibleForPickup,
    List<StoreHours>           OperationHours,
    List<SkuInventory>         Inventory
)
{
    /// <summary>Count of this SKU in stock at this store (0 if unknown).</summary>
    public int StockCount => Inventory.FirstOrDefault()?.Count ?? 0;

    /// <summary>Today's hours entry, matched by current local day-of-week abbreviation.</summary>
    public StoreHours? TodayHours
    {
        get
        {
            // API days: Sun Mon Tue Wed Thu Fri Sat
            var day = DateTime.Now.DayOfWeek switch
            {
                DayOfWeek.Sunday    => "Sun",
                DayOfWeek.Monday    => "Mon",
                DayOfWeek.Tuesday   => "Tue",
                DayOfWeek.Wednesday => "Wed",
                DayOfWeek.Thursday  => "Thu",
                DayOfWeek.Friday    => "Fri",
                DayOfWeek.Saturday  => "Sat",
                _                   => ""
            };
            return OperationHours.FirstOrDefault(h =>
                h.Day.Equals(day, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Whether the store is open right now, derived from OperationHours + local time.</summary>
    public bool? IsCurrentlyOpen
    {
        get
        {
            var today = TodayHours;
            if (today is null) return null;
            var now = DateTime.Now;
            int nowHHMM = now.Hour * 100 + now.Minute;
            if (!int.TryParse(today.Open,  out int open))  return null;
            if (!int.TryParse(today.Close, out int close)) return null;
            return nowHHMM >= open && nowHHMM < close;
        }
    }
}

public record SkuInventory(string Sku, int Count);
