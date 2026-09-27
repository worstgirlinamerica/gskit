using Spectre.Console;
using GSKit.Core.Http;
using GSKit.Core.Models;
using GSKit.Core.Scrapers.Inventory;
using System.Text.Json;

namespace GSKit.CLI.Commands;

public static class StockCommand
{
    public static async Task<int> RunAsync(string[] args, bool debug)
    {
        string? sku         = null;
        string? zip         = null;
        double? lat         = null;
        double? lon         = null;
        double  radius      = 100.0;
        bool    inStockOnly = false;
        string  format      = "table";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--zip":           zip         = args[++i]; break;
                case "--lat":           lat         = double.Parse(args[++i]); break;
                case "--long":          lon         = double.Parse(args[++i]); break;
                case "--radius":        radius      = double.Parse(args[++i]); break;
                case "--in-stock-only": inStockOnly = true; break;
                case "--format":        format      = args[++i]; break;
                default:
                    if (sku == null && !args[i].StartsWith('-'))
                        sku = args[i];
                    break;
            }
        }

        if (sku == null)
        {
            Err("SKU is required — e.g.  gskit stock 133857 --zip <zip>");
            return 1;
        }

        // ── Geocode ───────────────────────────────────────────────────────────
        double searchLat, searchLon;

        if (lat.HasValue && lon.HasValue)
        {
            searchLat = lat.Value;
            searchLon = lon.Value;
        }
        else if (zip != null)
        {
            Log("GEO", $"resolving {zip}");
            (searchLat, searchLon) = await GeocodingHelper.ZipToLatLonAsync(zip);
            if (debug) Log("GEO", $"{searchLat:F5}, {searchLon:F5}");
        }
        else
        {
            Err("provide --zip or --lat/--long");
            return 1;
        }

        // ── Query ─────────────────────────────────────────────────────────────
        Log("HTTP", $"Stores-FindStores  sku={sku}  radius={radius}mi");

        InventoryResult result;
        try
        {
            await using var client = new GameStopClient();
            var scraper = new StoreInventory(client);
            result = await scraper.FindStoresAsync(
                sku, searchLat, searchLon, radius,
                captureRawJson: debug);
        }
        catch (CloudflareBlockException ex)
        {
            Err(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Err(ex.Message);
            if (debug) AnsiConsole.WriteException(ex);
            return 1;
        }

        Log("OK", $"{result.TotalStores} stores · {result.InStockCount} in stock");

        if (debug && result.RawJson != null)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[dim]raw json[/]") { Justification = Justify.Left });
            AnsiConsole.WriteLine(result.RawJson);
            AnsiConsole.Write(new Rule() { Justification = Justify.Left });
            AnsiConsole.WriteLine();
        }

        AnsiConsole.WriteLine();

        return format.ToLower() switch
        {
            "json" => OutputJson(result),
            _      => OutputTable(result, inStockOnly),
        };
    }

    private static int OutputTable(InventoryResult r, bool inStockOnly)
    {
        var inStock  = r.InStock.ToList();
        var outStock = r.OutOfStock.ToList();

        AnsiConsole.Write(new Rule($"[green]IN STOCK[/] [dim]({inStock.Count})[/]")
            { Justification = Justify.Left });
        AnsiConsole.WriteLine();

        if (inStock.Count == 0)
            AnsiConsole.MarkupLine("  [dim]no stores with stock in this radius[/]");
        else
        {
            var t = MakeTable();
            foreach (var s in inStock) AddRow(t, s);
            AnsiConsole.Write(t);
        }

        if (!inStockOnly && outStock.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[dim]OUT OF STOCK ({outStock.Count})[/]")
                { Justification = Justify.Left });
            AnsiConsole.WriteLine();
            var t = MakeTable();
            foreach (var s in outStock) AddRow(t, s);
            AnsiConsole.Write(t);
        }
        else if (inStockOnly && outStock.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                $"  [dim]{outStock.Count} out-of-stock stores hidden  (remove --in-stock-only to show)[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule() { Justification = Justify.Left, Style = Style.Parse("dim") });
        AnsiConsole.MarkupLine(
            $"[dim]  sku {r.Sku}  ·  {r.RadiusMiles:F0}mi radius  ·  {r.FetchedAt:yyyy-MM-dd HH:mm} UTC[/]");
        AnsiConsole.WriteLine();

        return 0;
    }

    private static Table MakeTable() =>
        new Table()
            .BorderStyle(Style.Parse("dim"))
            .Border(TableBorder.Simple)
            .AddColumn(new TableColumn("[dim]STORE[/]"))
            .AddColumn(new TableColumn("[dim]CITY[/]"))
            .AddColumn(new TableColumn("[dim]ST[/]"))
            .AddColumn(new TableColumn("[dim]DIST[/]"))
            .AddColumn(new TableColumn("[dim]CONDITION[/]"))
            .AddColumn(new TableColumn("[dim]HOURS[/]"));

    private static void AddRow(Table table, Store s)
    {
        var inStockConds = s.ConditionsInStock
            .Where(c => c.IsInStock)
            .Select(c => c.DisplayName)
            .ToList();

        var condStr = inStockConds.Count > 0
            ? string.Join(", ", inStockConds)
            : (s.IsInStock ? "In Stock" : "—");

        var openStr = s.IsCurrentlyOpen switch
        {
            true  => $"open until {s.TodayClosingTime ?? "?"}",
            false => "closed",
            null  => "—",
        };

        var name  = Escape(s.Name.Length > 30 ? s.Name[..27] + "..." : s.Name);
        var city  = Escape(s.City);
        var state = Escape(s.StateCode);
        var dist  = $"{s.DistanceMiles:F1} mi";
        var cond  = Escape(condStr.Length > 20 ? condStr[..17] + "…" : condStr);
        var hours = Escape(openStr);

        string condMarkup = s.IsInStock
            ? (s.IsLimitedStock ? $"[yellow]{cond}[/]" : $"[green]{cond}[/]")
            : $"[dim]{cond}[/]";

        table.AddRow(name, city, state, $"[dim]{dist}[/]", condMarkup, $"[dim]{hours}[/]");
    }

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
            stores = r.Stores.Select(s => new
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
                is_open          = s.IsCurrentlyOpen,
                closes_at        = s.TodayClosingTime,
                conditions       = s.ConditionsInStock.Select(c => new
                {
                    name     = c.DisplayName,
                    in_stock = c.IsInStock,
                }),
                inventory = s.Inventory.Select(i => new { i.Sku, i.Count }),
            }),
        }, opts));
        return 0;
    }

    private static void Log(string tag, string msg) =>
        AnsiConsole.MarkupLine($"[dim][[{tag}]][/] {Escape(msg)}");

    private static void Err(string msg) =>
        AnsiConsole.MarkupLine($"[red][[ERROR]][/] {Escape(msg)}");

    private static string Escape(string s) =>
        s.Replace("[", "[[").Replace("]", "]]");
}
