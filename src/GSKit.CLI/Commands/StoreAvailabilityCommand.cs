using Spectre.Console;
using GSKit.Core.Http;
using GSKit.Core.Models;
using GSKit.Core.Scrapers;
using GSKit.CLI.Output;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace GSKit.CLI.Commands;

/// <summary>
/// gskit store-availability &lt;sku&gt; [--format json]
///
/// Calls Stores-ProductDetailStoreAvailability — the PDP endpoint that returns
/// per-variant (condition × platform × edition) availability at your preferred store,
/// plus nearestStoreDetailsObj showing which store actually has each variant.
///
/// Named after the endpoint, not a concept we invented.
/// NOTE: residential IP only (CF blocks datacenter).
/// </summary>
public static class StoreAvailabilityCommand
{
    public static async Task<int> RunAsync(string[] args, RunContext ctx)
    {
        string? sku    = null;
        string  format = "table";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--format": format = args[++i]; break;
                default:
                    if (sku is null && !args[i].StartsWith('-')) sku = args[i];
                    break;
            }
        }

        if (sku is null)
        {
            Log.Err("SKU required  →  gskit store-availability <sku>");
            return 1;
        }

        Log.Http($"Stores-ProductDetailStoreAvailability  sku={sku}");

        ProductDetailStoreAvailability? result;
        var sw = Stopwatch.StartNew();
        try
        {
            await using var client  = new GameStopClient(ctx.Debug);
            var scraper             = new StoreAvailabilityScraper(client);
            result                  = await scraper.GetAsync(sku);
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

        sw.Stop();

        if (result is null)
        {
            Log.Warn("no data returned");
            return 1;
        }

        Log.Ok($"fetched  [[{sw.ElapsedMilliseconds}ms]]");
        AnsiConsole.WriteLine();

        return format.ToLower() switch
        {
            "json" => OutputJson(result),
            _      => OutputTable(result),
        };
    }

    private static int OutputTable(ProductDetailStoreAvailability r)
    {
        var storeLabel = r.StoreName.Length > 0 ? r.StoreName : "preferred store";
        AnsiConsole.Write(new Rule($"[white bold]{Markup.Escape(storeLabel)}[/]  [dim]{Markup.Escape(r.StoreDetails)}[/]")
            { Justification = Justify.Left });
        AnsiConsole.WriteLine();

        var t = new Table()
            .BorderStyle(Style.Parse("grey23"))
            .Border(TableBorder.Simple)
            .AddColumn(new TableColumn("[dim]SKU[/]"))
            .AddColumn(new TableColumn("[dim]CONDITION[/]"))
            .AddColumn(new TableColumn("[dim]PLATFORM[/]"))
            .AddColumn(new TableColumn("[dim]EDITION[/]"))
            .AddColumn(new TableColumn("[dim]IN STOCK[/]"))
            .AddColumn(new TableColumn("[dim]QTY[/]") { Alignment = Justify.Right })
            .AddColumn(new TableColumn("[dim]NEAREST STORE[/]"))
            .AddColumn(new TableColumn("[dim]NEAREST QTY[/]") { Alignment = Justify.Right });

        foreach (var v in r.Variants)
        {
            var inStock      = v.InStock ? "[green]yes[/]" : "[dim]no[/]";
            var qty          = v.InStockCount > 0 ? $"[green]{v.InStockCount}[/]" : "[dim]0[/]";
            var nearestQty   = v.NearestStoreCount > 0 ? $"[green]{v.NearestStoreCount}[/]" : "[dim]0[/]";
            var nearestStore = v.NearestStoreName.Length > 0
                ? Markup.Escape(v.NearestStoreName.Length > 28
                    ? v.NearestStoreName[..25] + "…"
                    : v.NearestStoreName)
                : "[dim]—[/]";

            t.AddRow(
                $"[dim]{Markup.Escape(v.Sku)}[/]",
                v.Condition == "Pre-Owned" ? "[yellow]Pre-Owned[/]" : Markup.Escape(v.Condition),
                $"[dim]{Markup.Escape(v.Platform)}[/]",
                $"[dim]{Markup.Escape(v.Edition)}[/]",
                inStock,
                qty,
                nearestStore,
                nearestQty
            );
        }

        AnsiConsole.Write(t);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule() { Justification = Justify.Left, Style = Style.Parse("dim") });
        AnsiConsole.MarkupLine(
            $"[dim]  sku {r.Sku}  ·  Stores-ProductDetailStoreAvailability[/]");
        AnsiConsole.WriteLine();
        return 0;
    }

    private static int OutputJson(ProductDetailStoreAvailability r)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            sku                              = r.Sku,
            store_name                       = r.StoreName,
            store_details                    = r.StoreDetails,
            has_variants_available_for_pickup = r.HasVariantsAvailableForPickup,
            has_variants_in_stock            = r.HasVariantsInStock,
            variants = r.Variants.Select(v => new
            {
                sku              = v.Sku,
                condition        = v.Condition,
                platform         = v.Platform,
                edition          = v.Edition,
                in_stock         = v.InStock,
                in_stock_count   = v.InStockCount,
                allow_bops       = v.AllowBOPS,
                nearest_store_id = v.NearestStoreId,
                nearest_store    = v.NearestStoreName,
                nearest_qty      = v.NearestStoreCount,
            }),
        }, opts));
        return 0;
    }
}
