using System.CommandLine;
using GSKit.Core.Auth;
using GSKit.Core.Http;
using GSKit.Core.Models;
using GSKit.Core.Scrapers.Inventory;

namespace GSKit.CLI.Commands;

/// <summary>
/// gskit stock &lt;sku&gt; --zip <zip> --radius 100
/// gskit stock &lt;sku&gt; --lat <lat> --long <lon> --radius 100
/// gskit stock &lt;sku&gt; --zip <zip> --in-stock-only
/// gskit stock &lt;sku&gt; --zip <zip> --format json
/// </summary>
public static class StockCommand
{
    public static Command Build()
    {
        var skuArg    = new Argument<string>("sku",    "GameStop SKU (e.g. 133857)");
        var zipOpt    = new Option<string?>("--zip",   "US zip code to search from");
        var latOpt    = new Option<double?>("--lat",   "Latitude (alternative to --zip)");
        var lonOpt    = new Option<double?>("--long",  "Longitude (alternative to --zip)");
        var radOpt    = new Option<double>("--radius", () => 100.0, "Search radius in miles");
        var stockOnly = new Option<bool>("--in-stock-only", "Only show stores with stock");
        var formatOpt = new Option<string>("--format", () => "table", "Output: table | json | csv");
        var manualOpt = new Option<string?>("--session",
            "Manual session as 'dwsid=xxx;cf_clearance=yyy' (skips Chrome cookie read)");

        var cmd = new Command("stock", "Check in-store inventory for a SKU")
        {
            skuArg, zipOpt, latOpt, lonOpt, radOpt, stockOnly, formatOpt, manualOpt
        };

        cmd.SetHandler(async (sku, zip, lat, lon, radius, inStockOnly, format, session) =>
        {
            await using var sfcc = new SfccSession();

            // ── Auth ─────────────────────────────────────────────────────────
            if (session != null)
            {
                // Manual: --session "dwsid=xxx;cf_clearance=yyy"
                var parts = session.Split(';')
                    .Select(p => p.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

                sfcc.SeedManual(
                    parts.GetValueOrDefault("dwsid")      ?? throw new Exception("Missing dwsid in --session"),
                    parts.GetValueOrDefault("cf_clearance") ?? throw new Exception("Missing cf_clearance in --session")
                );
            }
            else
            {
                // Auto: read from Chrome cookie store
                try
                {
                    await sfcc.SeedFromChromeAsync();
                    Console.Error.WriteLine("[auth] Seeded from Chrome cookies ✓");
                }
                catch (FileNotFoundException)
                {
                    // No Chrome — try cold seed (works if CF is lenient)
                    Console.Error.WriteLine("[auth] Chrome not found — trying cold session seed...");
                    await sfcc.SeedColdAsync(sku);
                }
            }

            // ── Geocode ───────────────────────────────────────────────────────
            double searchLat, searchLon;
            if (lat.HasValue && lon.HasValue)
            {
                searchLat = lat.Value;
                searchLon = lon.Value;
            }
            else if (zip != null)
            {
                Console.Error.WriteLine($"[geo] Geocoding {zip}...");
                (searchLat, searchLon) = await GeocodingHelper.ZipToLatLonAsync(zip);
                Console.Error.WriteLine($"[geo] → {searchLat:F5}, {searchLon:F5}");
            }
            else
            {
                Console.Error.WriteLine("Error: provide --zip or --lat/--long");
                Environment.Exit(1);
                return;
            }

            // ── Query ─────────────────────────────────────────────────────────
            Console.Error.WriteLine($"[query] SKU {sku} within {radius}mi...");
            var scraper = new StoreInventory(sfcc);
            var result  = await scraper.FindStoresAsync(sku, searchLat, searchLon, radius);

            // ── Output ────────────────────────────────────────────────────────
            switch (format.ToLower())
            {
                case "json":
                    OutputJson(result);
                    break;
                case "csv":
                    OutputCsv(result, inStockOnly);
                    break;
                default:
                    OutputTable(result, inStockOnly);
                    break;
            }

        }, skuArg, zipOpt, latOpt, lonOpt, radOpt, stockOnly, formatOpt, manualOpt);

        return cmd;
    }

    // ── Table output ──────────────────────────────────────────────────────────
    private static void OutputTable(InventoryResult r, bool inStockOnly)
    {
        var inStock  = r.StoresWithStock;
        var outStock = inStockOnly ? [] : r.StoresWithoutStock;

        Console.WriteLine();
        Console.WriteLine($"  SKU {r.Sku}  ·  {r.TotalStores} stores within {r.RadiusMiles}mi  ·  {r.FetchedAt:HH:mm} UTC");
        Console.WriteLine();

        if (inStock.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ✗  No stores with stock found.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓  IN STOCK at {inStock.Count} location(s):");
            Console.ResetColor();
            Console.WriteLine();

            foreach (var s in inStock)
                PrintStoreRow(s, inStock: true);
        }

        if (outStock.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  ✗  Out of stock at {outStock.Count} store(s):");
            Console.ResetColor();
            foreach (var s in outStock)
                PrintStoreRow(s, inStock: false);
        }

        Console.WriteLine();
    }

    private static void PrintStoreRow(Store s, bool inStock)
    {
        var status = inStock
            ? (s.IsLimitedStock ? "⚠ LOW " : "✓ IN  ")
            : "✗ OUT ";

        var color = inStock
            ? (s.IsLimitedStock ? ConsoleColor.Yellow : ConsoleColor.Green)
            : ConsoleColor.DarkGray;

        var conditions = s.ConditionsInStock
            .Where(c => c.IsInStock)
            .Select(c => c.Condition)
            .DefaultIfEmpty("?")
            .Aggregate((a, b) => $"{a}, {b}");

        var open = s.IsCurrentlyOpen
            ? $"Open until {s.TodayClosingTime}"
            : "Closed";

        Console.ForegroundColor = color;
        Console.Write($"  {status}");
        Console.ResetColor();
        Console.Write($" {s.Name,-30}");
        Console.Write($"  {s.DistanceMiles,6:F1} mi");
        Console.Write($"  {s.City + ", " + s.State,-18}");
        Console.Write($"  {conditions,-12}");
        Console.Write($"  {open}");
        if (s.Phone != null) Console.Write($"  {s.Phone}");
        Console.WriteLine();
    }

    // ── JSON output ───────────────────────────────────────────────────────────
    private static void OutputJson(InventoryResult r)
    {
        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(r, opts));
    }

    // ── CSV output ────────────────────────────────────────────────────────────
    private static void OutputCsv(InventoryResult r, bool inStockOnly)
    {
        Console.WriteLine("StoreId,Name,Address,City,State,Zip,Phone,Distance,InStock,LimitedStock,Conditions,CurrentlyOpen,ClosingTime");
        var stores = inStockOnly ? r.StoresWithStock : r.StoresWithStock.Concat(r.StoresWithoutStock);
        foreach (var s in stores)
        {
            var conds = string.Join("|", s.ConditionsInStock.Where(c => c.IsInStock).Select(c => c.Condition));
            Console.WriteLine(string.Join(",",
                s.Id, $"\"{s.Name}\"",
                $"\"{s.Address1} {s.Address2}\"".Trim(),
                s.City, s.State, s.PostalCode, s.Phone ?? "",
                s.DistanceMiles, s.IsInStock, s.IsLimitedStock,
                conds, s.IsCurrentlyOpen, s.TodayClosingTime ?? ""
            ));
        }
    }
}
