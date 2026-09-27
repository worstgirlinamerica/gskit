namespace GSKit.Core.Models;

/// <summary>
/// One result from Constructor.io Search (ac.cnstrc.com/search).
/// variation_id in the Constructor response is the GameStop SKU.
/// </summary>
public record SearchResult(
    string  Sku,
    string  Name,
    string  Platform,
    string  Condition,
    string  Edition,
    decimal Price,
    string  Brand,
    string  Url,
    string  ImageUrl,
    int     VariationCount
);

/// <summary>
/// One result from Tile-GetProductsJSON — lightweight tile data for a SKU.
/// Keyed by SKU in the response's productsJSON map.
/// </summary>
public record TileProduct(
    string   Sku,
    string   Name,
    decimal? PriceBase,
    decimal? PriceSale,
    decimal? PricePro,
    bool     Available,
    bool     ReadyToOrder,
    bool     AllowBOPS,
    bool     AllowSDD,
    bool     IsDigital,
    string   Url,
    string   ImageUrl
);

/// <summary>
/// Per-variant availability at the preferred store from Stores-ProductDetailStoreAvailability.
/// </summary>
public record VariantAvailability(
    string Sku,
    string Condition,
    string Platform,
    string Edition,
    bool   InStock,
    int    InStockCount,
    bool   AllowBOPS,
    string NearestStoreId,
    string NearestStoreName,
    int    NearestStoreCount
);

public record ProductDetailStoreAvailability(
    string                     Sku,
    string                     StoreName,
    string                     StoreDetails,
    bool                       HasVariantsAvailableForPickup,
    bool                       HasVariantsInStock,
    List<VariantAvailability>  Variants
);

/// <summary>
/// One result from Trade-GetSuggestions — the trade wizard's product search.
/// ProductId here is GameStop's internal trade system ID, NOT the SKU used
/// by other endpoints. Pass it to Trade-Show to get cash/credit values.
/// </summary>
public record TradeSuggestion(
    string ProductId,
    string Name,
    string ImageUrl
);

/// <summary>
/// Trade-in value breakdown from Trade-Show (server-rendered HTML partial).
/// CashValue    — what GS pays you in cash
/// CreditValue  — what GS pays as in-store credit (always higher)
/// ProBonusValue — additional credit for Pro members on top of CreditValue
/// </summary>
public record TradeValue(
    string  ProductId,
    string  Condition,
    string  ProductName,
    decimal CashValue,
    decimal CreditValue,
    decimal ProBonusValue,
    string  RawHtml
);
