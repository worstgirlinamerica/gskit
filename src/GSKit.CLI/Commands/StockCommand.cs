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
            Log.Dbg($"geocode  {searchLat:F5}, {searchLon:F5}  ({sw.ElapsedMilliseconds}ms)", ctx.Debug);
        }
        else
        {
            Log.Err("provide --zip <code>  or  --lat / --long");
            return 1;
        }

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

        var inCount = result.InStockCount;
        var total   = result.TotalStores;
        Log.Ok($"{total} stores  ·  {Log.Hl(inCount > 0 ? $"{inCount} in stock" : "0 in stock", inCount > 0 ? "green bold" : "dim")}  [{timer.ElapsedMilliseconds}ms]");

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

    private static int OutputTable(InventoryResult r, bool inStockOnly, bool verbose)
    {
        var inStock  = r.InStock.ToList();
        var outStock = r.OutOfStock.ToList();

        if (inStock.Count > 0)
        {
            AnsiConsole.Write(new Rule($"[green]IN STOCK[/] [dim]({inStock.Count})[/]")
                { Justification = Justify.Left });
            AnsiConsole.WriteLine();
            var t = MakeTable(verbose);
            foreach (var s in inStock) AddRow(t, s, inStock: true, verbose);
            AnsiConsole.Write(t);
        }
        else
        {
            AnsiConsole.Write(new Rule("[dim]IN STOCK (0)[/]") { Justification = Justify.Left });
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]  no stores within the search radius have this SKU[/]");
        }

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
            .AddColumn(new TableColumn("[dim]STORE[/]"))
            .AddColumn(new TableColumn("[dim]CITY[/]"))
            .AddColumn(new TableColumn("[dim]ST[/]"))
            .AddColumn(new TableColumn("[dim]DIST[/]") { Alignment = Justify.Right })
            .AddColumn(new TableColumn("[dim]QTY[/]")  { Alignment = Justify.Right })
            .AddColumn(new TableColumn("[dim]CONDITION[/]"))
            .AddColumn(new TableColumn("[dim]TODAY[/]"));

        if (verbose)
        {
            t.AddColumn(new TableColumn("[dim]ADDRESS[/]"));
            t.AddColumn(new TableColumn("[dim]PHONE[/]"));
        }

        return t;
    }

    private static void AddRow(Table table, Store s, bool inStock, bool verbose)
    {
        var inStockConds = s.ConditionsEligibleForPickup
            .Where(c => c.IsInStock)
            .Select(c => c.DisplayName)
            .ToList();

        var condStr = inStockConds.Count > 0
            ? string.Join(", ", inStockConds)
            : (s.IsInStock ? "In Stock" : "—");

        // Hours: show today's open–close range, with open/closed indicator
        string hoursStr;
        var today = s.TodayHours;
        if (today is not null)
        {
            var isOpen = s.IsCurrentlyOpen;
            var range  = today.Display;
            hoursStr = isOpen switch
            {
                true  => $"open  {range}",
                false => $"closed  {range}",
                null  => range,
            };
        }
        else
        {
            hoursStr = "—";
        }

        var city = TitleCase(s.City);
        var name = Markup.Escape(s.Name.Length > 28 ? s.Name[..25] + "…" : s.Name);
        var dist = $"{s.DistanceMiles:F1} mi";
        var cond = Markup.Escape(condStr.Length > 22 ? condStr[..19] + "…" : condStr);
        var qty  = s.StockCount > 0 ? $"{s.StockCount}" : "—";

        string condMarkup = inStock
            ? (s.IsLimitedStock ? $"[yellow]{cond}[/]" : $"[green]{cond}[/]")
            : $"[dim]{cond}[/]";

        string qtyMarkup = inStock && s.StockCount > 0
            ? (s.StockCount <= 2 ? $"[yellow]{qty}[/]" : $"[green]{qty}[/]")
            : $"[dim]{qty}[/]";

        string hoursMarkup = today is not null
            ? s.IsCurrentlyOpen switch
            {
                true  => $"[green]open[/]  [dim]{Markup.Escape(today.Display)}[/]",
                false => $"[dim]closed  {Markup.Escape(today.Display)}[/]",
                null  => $"[dim]{Markup.Escape(hoursStr)}[/]",
            }
            : "[dim]—[/]";

        var cells = new List<string>
        {
            name,
            Markup.Escape(city),
            Markup.Escape(s.StateCode.ToUpper()),
            $"[dim]{Markup.Escape(dist)}[/]",
            qtyMarkup,
            condMarkup,
            hoursMarkup,
        };

        if (verbose)
        {
            var addr = s.Address2 is { Length: > 0 }
                ? $"{s.Address1}, {s.Address2}" : s.Address1;
            cells.Add($"[dim]{Markup.Escape(addr)}[/]");
            cells.Add($"[dim]{Markup.Escape(s.Phone ?? "—")}[/]");
        }

        table.AddRow(cells.ToArray());
    }

    private static int OutputJson(InventoryResult r)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        // JSON goes to stdout only — no log lines, safe to pipe/redirect
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
                stock_count      = s.StockCount,
                is_limited_stock = s.IsLimitedStock,
                is_open          = s.IsCurrentlyOpen,
                today_hours      = s.TodayHours?.Display,
                conditions       = s.ConditionsEligibleForPickup.Select(c => new
                {
                    name     = c.DisplayName,
                    in_stock = c.IsInStock,
                }),
                hours = s.OperationHours.Select(h => new
                {
                    day     = h.Day,
                    hours   = h.Display,
                }),
            }),
        }, opts));
        return 0;
    }

    private static string TitleCase(string s) =>
        CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower());
}
