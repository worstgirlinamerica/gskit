using Spectre.Console;
using GSKit.Core.Http;
using GSKit.Core.Models;
using GSKit.Core.Scrapers.Inventory;
using GSKit.CLI.Output;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace GSKit.CLI.Commands;

public static class StockCommand
{
    public static async Task<int> RunAsync(string[] args, RunContext ctx)
    {
        // ── Parse args ────────────────────────────────────────────────────────
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
                    if (sku is null && !args[i].StartsWith('-'))
                        sku = args[i];
                    break;
            }
        }

        if (sku is null)
        {
            Log.Err("SKU required  →  gskit stock <sku> --zip <code>");
            return 1;
        }

        // ── Geocode ───────────────────────────────────────────────────────────
        double searchLat, searchLon;

        if (lat.HasValue && lon.HasValue)
        {
            searchLat = lat.Value;
            searchLon = lon.Value;
            Log.Dbg($"coords from args  {searchLat:F5}, {searchLon:F5}", ctx.Debug);
        }
        else if (zip is not null)
        {
            Log.Geo($"resolving {zip}");
            var sw = Stopwatch.StartNew();
            (searchLat, searchLon) = await GeocodingHelper.ZipToLatLonAsync(zip);
            Log.Dbg($"geocode in {sw.ElapsedMilliseconds}ms  →  {searchLat:F5}, {searchLon:F5}", ctx.Debug);
        }
        else
        {
            Log.Err("provide --zip <code>  or  --lat / --long");
            return 1;
        }

        // ── Fetch ─────────────────────────────────────────────────────────────
        Log.Http($"Stores-FindStores  sku={sku}  radius={radius:F0}mi");

        InventoryResult result;
        var timer = Stopwatch.StartNew();
        try
        {
            await using var client = new GameStopClient(ctx.Debug);
            var scraper = new StoreInventory(client);
            result = await scraper.FindStoresAsync(
                sku, searchLat, searchLon, radius,
                captureRawJson: ctx.Debug);
        }
        catch (CloudflareBlockException ex)
        {
            Log.Err(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Log.Err(ex.Message);
            if (ctx.Debug) AnsiConsole.WriteException(ex);
            return 1;
        }

        timer.Stop();
        Log.Ok($"{result.TotalStores} stores · {result.InStockCount} in stock  [{timer.ElapsedMilliseconds}ms]");

        // ── Debug dump ────────────────────────────────────────────────────────
        if (ctx.Debug && result.RawJson is not null)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[dim]raw json[/]") { Justification = Justify.Left });
            AnsiConsole.WriteLine(result.RawJson);
            AnsiConsole.Write(new Rule() { Justification = Justify.Left, Style = Style.Parse("dim") });
            AnsiConsole.WriteLine();
        }

        AnsiConsole.WriteLine();

        return format.ToLower() switch
        {
            "json" => OutputJson(result),
            _      => OutputTable(result, inStockOnly, ctx.Verbose),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Table renderer
    // ─────────────────────────────────────────────────────────────────────────

    private static int OutputTable(InventoryResult r, bool inStockOnly, bool verbose)
    {
        var inStock  = r.InStock.ToList();
        var outStock = r.OutOfStock.ToList();

        // ── IN STOCK block ────────────────────────────────────────────────────
        if (inStock.Count > 0)
        {
            AnsiConsole.Write(new Rule($"[green bold]IN STOCK[/] [dim]({inStock.Count})[/]")
                { Justification = Justify.Left });
            AnsiConsole.WriteLine();

            var t = MakeTable(verbose);
            foreach (var s in inStock) AddRow(t, s, inStock: true, verbose);
            AnsiConsole.Write(t);
        }
        else
        {
            AnsiConsole.Write(new Rule($"[dim]IN STOCK (0)[/]") { Justification = Justify.Left });
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]  no stores carrying this SKU within the search radius[/]");
        }

        // ── OUT OF STOCK block ────────────────────────────────────────────────
        if (!inStockOnly)
        {
            if (outStock.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[dim]OUT OF STOCK ({outStock.Count})[/]")
                    { Justification = Justify.Left });
                AnsiConsole.WriteLine();
                var t = MakeTable(verbose);
                foreach (var s in outStock) AddRow(t, s, inStock: false, verbose);
                AnsiConsole.Write(t);
            }
        }
        else if (outStock.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                $"  [dim]{outStock.Count} out-of-stock store{(outStock.Count == 1 ? "" : "s")} hidden  (--in-stock-only)[/]");
        }

        // ── Footer ────────────────────────────────────────────────────────────
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule() { Justification = Justify.Left, Style = Style.Parse("dim") });
        AnsiConsole.MarkupLine(
            $"[dim]  sku {r.Sku}  ·  {r.RadiusMiles:F0}mi radius  ·  {r.FetchedAt:yyyy-MM-dd HH:mm} UTC[/]");
        AnsiConsole.WriteLine();

        return 0;
    }

    private static Table MakeTable(bool verbose)
    {
        var t = new Table()
            .BorderStyle(Style.Parse("grey23"))
            .Border(TableBorder.Simple)
            .AddColumn(new TableColumn("[grey85]STORE[/]"))
            .AddColumn(new TableColumn("[grey85]CITY[/]"))
            .AddColumn(new TableColumn("[grey85]ST[/]"))
            .AddColumn(new TableColumn("[grey85]DIST[/]") { Alignment = Justify.Right })
            .AddColumn(new TableColumn("[grey85]CONDITION[/]"))
            .AddColumn(new TableColumn("[grey85]HOURS[/]"));

        if (verbose)
        {
            t.AddColumn(new TableColumn("[grey85]ADDRESS[/]"));
            t.AddColumn(new TableColumn("[grey85]PHONE[/]"));
        }

        return t;
    }

    private static void AddRow(Table table, Store s, bool inStock, bool verbose)
    {
        // Condition string
        var inStockConds = s.ConditionsInStock
            .Where(c => c.IsInStock)
            .Select(c => c.DisplayName)
            .ToList();

        var condStr = inStockConds.Count > 0
            ? string.Join(", ", inStockConds)
            : (s.IsInStock ? "In Stock" : "—");

        // Hours string
        var hoursStr = s.IsCurrentlyOpen switch
        {
            true  => $"open until {s.TodayClosingTime ?? "?"}",
            false => "closed",
            null  => "—",
        };

        // City / state — normalize ALL-CAPS that GameStop DB returns
        var city  = TitleCase(s.City);
        var state = s.StateCode.ToUpper();
        var name  = Markup.Escape(s.Name.Length > 28 ? s.Name[..25] + "…" : s.Name);
        var dist  = $"{s.DistanceMiles:F1} mi";
        var cond  = Markup.Escape(condStr.Length > 22 ? condStr[..19] + "…" : condStr);
        var hours = Markup.Escape(hoursStr);

        // Condition cell — green/yellow/dim
        string condMarkup = inStock
            ? (s.IsLimitedStock ? $"[yellow]{cond}[/]" : $"[green]{cond}[/]")
            : $"[dim]{cond}[/]";

        // Preferred store indicator
        string nameMarkup = s.IsPreferredStore
            ? $"[cyan bold]{name}[/] [dim cyan]★[/]"
            : name;

        var cells = new List<string>
        {
            nameMarkup,
            Markup.Escape(city),
            Markup.Escape(state),
            $"[dim]{Markup.Escape(dist)}[/]",
            condMarkup,
            $"[dim]{hours}[/]",
        };

        if (verbose)
        {
            var addr = s.Address2 is { Length: > 0 }
                ? $"{s.Address1}, {s.Address2}"
                : s.Address1;
            cells.Add($"[dim]{Markup.Escape(addr)}[/]");
            cells.Add($"[dim]{Markup.Escape(s.Phone ?? "—")}[/]");
        }

        table.AddRow(cells.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JSON renderer
    // ─────────────────────────────────────────────────────────────────────────

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
                address          = $"{s.Address1}{(s.Address2 is { Length: > 0 } ? ", " + s.Address2 : "")}",
                city             = TitleCase(s.City),
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

    private static string TitleCase(string s) =>
        CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower());
}
