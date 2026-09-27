using Spectre.Console;
using GSKit.Core.Http;
using GSKit.Core.Models;
using GSKit.Core.Scrapers;
using GSKit.CLI.Output;
using System.Diagnostics;
using System.Text.Json;

namespace GSKit.CLI.Commands;

/// <summary>
/// gskit tiles &lt;sku1&gt; [sku2 sku3 ...] [--format json]
///
/// Hits Tile-GetProductsJSON with a batch of SKUs — the same endpoint the PDP
/// uses to populate "More Like This" carousels. Returns name, base/sale/pro price,
/// and availability flags (BOPS, SDD) for each SKU in one request.
///
/// Good for bulk price checks without a full Product-Variation call per SKU.
/// NOTE: residential IP only (CF blocks datacenter).
/// </summary>
public static class TilesCommand
{
    public static async Task<int> RunAsync(string[] args, RunContext ctx)
    {
        var skus   = new List<string>();
        string fmt = "table";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--format": fmt = args[++i]; break;
                default:
                    if (!args[i].StartsWith('-')) skus.Add(args[i]);
                    break;
            }
        }

        if (skus.Count == 0)
        {
            Log.Err("at least one SKU required  →  gskit tiles <sku> [sku2 sku3 ...]");
            return 1;
        }

        Log.Http($"Tile-GetProductsJSON  skus={string.Join(",", skus)}");

        List<TileProduct> tiles;
        var sw = Stopwatch.StartNew();
        try
        {
            await using var client  = new GameStopClient(ctx.Debug);
            var scraper             = new TileScraper(client);
            tiles                   = await scraper.GetTilesAsync(skus);
        }
        catch (CloudflareBlockException ex)
        {
            Log.Err(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Log.Err(ex.Message);
            return 1;
        }

        sw.Stop();
        Log.Ok($"{tiles.Count} tiles  [[{sw.ElapsedMilliseconds}ms]]");
        AnsiConsole.WriteLine();

        return fmt.ToLower() switch
        {
            "json" => OutputJson(tiles),
            _      => OutputTable(tiles),
        };
    }

    private static int OutputTable(List<TileProduct> tiles)
    {
        if (tiles.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]  no tile data returned[/]");
            AnsiConsole.WriteLine();
            return 0;
        }

        var t = new Table()
            .BorderStyle(Style.Parse("grey23"))
            .Border(TableBorder.Simple)
            .AddColumn(new TableColumn("[dim]SKU[/]"))
            .AddColumn(new TableColumn("[dim]NAME[/]"))
            .AddColumn(new TableColumn("[dim]BASE[/]")  { Alignment = Justify.Right })
            .AddColumn(new TableColumn("[dim]SALE[/]")  { Alignment = Justify.Right })
            .AddColumn(new TableColumn("[dim]PRO[/]")   { Alignment = Justify.Right })
            .AddColumn(new TableColumn("[dim]AVAIL[/]"))
            .AddColumn(new TableColumn("[dim]BOPS[/]"))
            .AddColumn(new TableColumn("[dim]SDD[/]"));

        foreach (var tile in tiles)
        {
            var name    = tile.Name.Length > 40 ? tile.Name[..37] + "…" : tile.Name;
            var avail   = tile.Available ? "[green]yes[/]" : "[dim]no[/]";
            var bops    = tile.AllowBOPS ? "[green]yes[/]" : "[dim]no[/]";
            var sdd     = tile.AllowSDD  ? "[green]yes[/]" : "[dim]no[/]";
            var baseStr = tile.PriceBase.HasValue ? $"${tile.PriceBase:F2}" : "—";
            var saleStr = tile.PriceSale.HasValue ? $"[green]${tile.PriceSale:F2}[/]" : "[dim]—[/]";
            var proStr  = tile.PricePro.HasValue  ? $"[cyan]${tile.PricePro:F2}[/]"   : "[dim]—[/]";

            t.AddRow(
                $"[dim]{Markup.Escape(tile.Sku)}[/]",
                Markup.Escape(name),
                $"[dim]{Markup.Escape(baseStr)}[/]",
                saleStr,
                proStr,
                avail, bops, sdd
            );
        }

        AnsiConsole.Write(t);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule() { Justification = Justify.Left, Style = Style.Parse("dim") });
        AnsiConsole.MarkupLine($"[dim]  Tile-GetProductsJSON  ·  {tiles.Count} skus[/]");
        AnsiConsole.WriteLine();
        return 0;
    }

    private static int OutputJson(List<TileProduct> tiles)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(JsonSerializer.Serialize(tiles.Select(t => new
        {
            sku          = t.Sku,
            name         = t.Name,
            price_base   = t.PriceBase,
            price_sale   = t.PriceSale,
            price_pro    = t.PricePro,
            available    = t.Available,
            ready_to_order = t.ReadyToOrder,
            allow_bops   = t.AllowBOPS,
            allow_sdd    = t.AllowSDD,
            is_digital   = t.IsDigital,
        }), opts));
        return 0;
    }
}
