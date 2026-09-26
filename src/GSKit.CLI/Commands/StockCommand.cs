using Spectre.Console;
using GSKit.Core.Auth;
using GSKit.Core.Http;
using GSKit.Core.Models;
using GSKit.Core.Scrapers.Inventory;
using System.Text.Json;

namespace GSKit.CLI.Commands;

public static class StockCommand
{
    public static async Task<int> RunAsync(string[] args, bool debug)
    {
        // ── Arg parse ─────────────────────────────────────────────────────────
        string? sku         = null;
        string? zip         = null;
        double? lat         = null;
        double? lon         = null;
        double  radius      = 100.0;
        bool    inStockOnly = false;
        string  format      = "table";
        string? session     = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--zip":          zip         = args[++i]; break;
                case "--lat":          lat         = double.Parse(args[++i]); break;
                case "--long":         lon         = double.Parse(args[++i]); break;
                case "--radius":       radius      = double.Parse(args[++i]); break;
                case "--in-stock-only": inStockOnly = true; break;
                case "--format":       format      = args[++i]; break;
                case "--session":      session     = args[++i]; break;
                default:
                    if (sku == null && !args[i].StartsWith('-'))
                        sku = args[i];
                    break;
            }
        }

        if (sku == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] SKU is required  (e.g. gskit stock 133857 --zip <zip>)");
            return 1;
        }

        // ── Auth ──────────────────────────────────────────────────────────────
        await using var sfcc = new SfccSession();

        if (session != null)
        {
            Step("auth", "Using manual session cookies");
            var parts = session.Split(';')
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
            sfcc.SeedManual(
                parts.GetValueOrDefault("dwsid")        ?? throw new Exception("--session missing dwsid"),
                parts.GetValueOrDefault("cf_clearance") ?? throw new Exception("--session missing cf_clearance")
            );
            Ok("auth", "manual session seeded");
        }
        else
        {
            Step("auth", "Reading Chrome cookies...");
            try
            {
                await sfcc.SeedFromChromeAsync();
                Ok("auth", $"dwsid captured from Chrome");
            }
            catch (FileNotFoundException)
            {
                Warn("auth", "Chrome not found — trying cold seed (no cf_clearance)");
                await sfcc.SeedColdAsync(sku);
                if (!sfcc.IsSeeded)
                {
                    AnsiConsole.MarkupLine("[red]✗ auth[/]  Could not seed session. Pass --session 'dwsid=...;cf_clearance=...'");
                    return 1;
                }
                Ok("auth", "cold seed succeeded");
            }
        }

        // ── Geocode ───────────────────────────────────────────────────────────
        double searchLat, searchLon;

        if (lat.HasValue && lon.HasValue)
        {
            searchLat = lat.Value;
            searchLon = lon.Value;
            if (debug) Step("geo", $"Using provided coords: {searchLat}, {searchLon}");
        }
        else if (zip != null)
        {
            Step("geo", $"Geocoding {zip}...");
            (searchLat, searchLon) = await GeocodingHelper.ZipToLatLonAsync(zip);
            Ok("geo", $"{searchLat:F5}, {searchLon:F5}");
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Error:[/] provide --zip or --lat/--long");
            return 1;
        }

        // ── Query ─────────────────────────────────────────────────────────────
        Step("query", $"Stores-FindStores  SKU={sku}  radius={radius}mi...");

        InventoryResult result;
        try
        {
            var scraper = new StoreInventory(sfcc);
            result = await scraper.FindStoresAsync(
                sku, searchLat, searchLon, radius,
                captureRawJson: debug);
        }
        catch (CloudflareBlockException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ CF block:[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ error:[/] {ex.Message}");
            if (debug) AnsiConsole.WriteException(ex);
            return 1;
        }

        Ok("query", $"200 OK — {result.TotalStores} stores  ({result.InStockCount} in stock)");

        // ── Debug: raw JSON ───────────────────────────────────────────────────
        if (debug && result.RawJson != null)
        {
            AnsiConsole.MarkupLine("\n[dim]── raw JSON ──────────────────────────────────────────────[/]");
            AnsiConsole.WriteLine(result.RawJson);
            AnsiConsole.MarkupLine("[dim]─────────────────────────────────────────────────────────[/]\n");
        }

        // ── Output ────────────────────────────────────────────────────────────
        AnsiConsole.WriteLine();

        return format.ToLower() switch
        {
            "json" => OutputJson(result),
            _      => OutputTable(result, inStockOnly),
        };
    }

    // ── Table output ──────────────────────────────────────────────────────────
    private static int OutputTable(InventoryResult r, bool inStockOnly)
    {
        // Header
        AnsiConsole.MarkupLine(
            $"  [bold]SKU {r.Sku}[/]  [dim]·[/]  " +
            $"[bold]{r.TotalStores}[/] stores within [bold]{r.RadiusMiles:F0}mi[/]  [dim]·[/]  " +
            $"[dim]{r.FetchedAt:HH:mm} UTC[/]");
        AnsiConsole.WriteLine();

        var inStock  = r.InStock.ToList();
        var outStock = r.OutOfStock.ToList();

        // ── In-stock section
        if (inStock.Count == 0)
        {
            AnsiConsole.MarkupLine("  [red]✗  No stores with stock in this radius.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"  [green]✓  IN STOCK at {inStock.Count} location(s):[/]");
            AnsiConsole.WriteLine();
            foreach (var s in inStock)
                PrintRow(s);
        }

        // ── Out-of-stock section
        // Always shown unless --in-stock-only. We never drop stores from the
        // API response — if GameStop returned them they're real locations even
        // if the specific SKU isn't available there right now.
        if (!inStockOnly && outStock.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [dim]✗  Out of stock / not available at {outStock.Count} store(s):[/]");
            AnsiConsole.WriteLine();
            foreach (var s in outStock)
                PrintRow(s);
        }
        else if (inStockOnly && outStock.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                $"  [dim]({outStock.Count} out-of-stock stores hidden — remove --in-stock-only to see them)[/]");
        }

        // Footer
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [dim]fetched {r.FetchedAt:yyyy-MM-dd HH:mm} UTC · {r.RadiusMiles:F0}mi radius · SKU {r.Sku}[/]");
        AnsiConsole.WriteLine();

        return 0;
    }

    private static void PrintRow(Store s)
    {
        var (badge, color) = s.IsInStock
            ? (s.IsLimitedStock ? "⚠ LOW " : "✓ IN  ", s.IsLimitedStock ? "yellow" : "green")
            : ("✗ OUT ", "red");

        // Which conditions are in stock — fall back to "—" if none
        var inStockConds = s.ConditionsInStock
            .Where(c => c.IsInStock)
            .Select(c => c.DisplayName)
            .ToList();
        var condStr = inStockConds.Count > 0
            ? string.Join(", ", inStockConds)
            : (s.IsInStock ? "In Stock" : "—");

        // Hours / open status
        var openStr = s.IsCurrentlyOpen.HasValue
            ? (s.IsCurrentlyOpen.Value
                ? $"Open until {s.TodayClosingTime ?? "?"}"
                : "Closed now")
            : "";

        var dist  = $"{s.DistanceMiles,5:F1} mi";
        var loc   = Escape($"{s.City}, {s.StateCode}");
        var name  = Escape(s.Name.Length > 28 ? s.Name[..25] + "..." : s.Name);
        var phone = s.Phone ?? "";
        var cond  = Escape(condStr.Length > 18 ? condStr[..15] + "…" : condStr);

        AnsiConsole.MarkupLine(
            $"  [{color}]{badge}[/]" +
            $" {name,-28}" +
            $"  [dim]{dist}[/]" +
            $"  {loc,-18}" +
            $"  [cyan]{cond,-18}[/]" +
            $"  [dim]{Escape(openStr),-24}[/]" +
            $"  [dim]{Escape(phone)}[/]"
        );
    }

    // ── JSON output ───────────────────────────────────────────────────────────
    private static int OutputJson(InventoryResult r)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            sku            = r.Sku,
            query_lat      = r.QueryLat,
            query_lon      = r.QueryLon,
            radius_miles   = r.RadiusMiles,
            fetched_at     = r.FetchedAt,
            total_stores   = r.TotalStores,
            in_stock_count = r.InStockCount,
            stores         = r.Stores.Select(s => new
            {
                id               = s.Id,
                name             = s.Name,
                address          = $"{s.Address1}{(s.Address2 != null ? ", " + s.Address2 : "")}",
                city             = s.City,
                state            = s.StateCode,
                postal_code      = s.PostalCode,
                phone            = s.Phone,
                distance_miles   = s.DistanceMiles,
                is_in_stock      = s.IsInStock,
                is_limited_stock = s.IsLimitedStock,
                is_preferred     = s.IsPreferredStore,
                is_currently_open = s.IsCurrentlyOpen,
                today_closing    = s.TodayClosingTime,
                conditions       = s.ConditionsInStock.Select(c => new
                {
                    display   = c.DisplayName,
                    in_stock  = c.IsInStock,
                }),
                inventory_counts = s.Inventory.Select(i => new
                {
                    sku   = i.Sku,
                    count = i.Count,
                }),
                pickup = s.PickupDetails == null ? null : (object)new
                {
                    bops = s.PickupDetails.BopsEnabled,
                    ispu = s.PickupDetails.IspuEnabled,
                    hops = s.PickupDetails.HopsEnabled,
                },
            }),
        }, opts));
        return 0;
    }

    // ── Progress helpers ──────────────────────────────────────────────────────
    private static void Step(string tag, string msg) =>
        AnsiConsole.MarkupLine($"  [dim]●[/] [grey]{tag,-6}[/]  {msg}");

    private static void Ok(string tag, string msg) =>
        AnsiConsole.MarkupLine($"  [green]✓[/] [grey]{tag,-6}[/]  [dim]{msg}[/]");

    private static void Warn(string tag, string msg) =>
        AnsiConsole.MarkupLine($"  [yellow]⚠[/] [grey]{tag,-6}[/]  {msg}");

    /// Escape Spectre.Console markup chars in user data
    private static string Escape(string s) =>
        s.Replace("[", "[[").Replace("]", "]]");
}
