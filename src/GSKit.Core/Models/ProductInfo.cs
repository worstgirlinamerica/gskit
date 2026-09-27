namespace GSKit.Core.Models;

public sealed class ProductInfo
{
    public string Sku               { get; init; } = "";
    public string Title             { get; init; } = "";
    public string Condition         { get; init; } = "";
    public string Platform          { get; init; } = "";
    public string PriceFormatted    { get; init; } = "";
    public string? ProPriceFormatted{ get; init; }
    public decimal PriceValue       { get; init; }
    public bool   Available         { get; init; }
    public bool   ReadyToOrder      { get; init; }
    public string AvailabilityMessage { get; init; } = "";
    public bool   IsPreorder        { get; init; }
    public bool   IsBackorder       { get; init; }
    public bool   IsTradeable       { get; init; }
    public decimal TradeBasePrice   { get; init; }
    public string? Publisher        { get; init; }
    public string? Developer        { get; init; }
    public string? Genre            { get; init; }
    public string? ReleaseDate      { get; init; }
}

public sealed class SameDayResult
{
    public bool   HideSdd      { get; init; }
    public bool   NearBy       { get; init; }
    public bool   AtcDisable   { get; init; }
    public bool   ProductAvail { get; init; }
    public bool   IsTimeCutOff { get; init; }
    public string TimeLeft     { get; init; } = "";
}
