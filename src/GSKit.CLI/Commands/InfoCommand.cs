using Spectre.Console;
using GSKit.Core.Http;
using GSKit.Core.Models;
using GSKit.Core.Scrapers;
using GSKit.CLI.Output;
using System.Diagnostics;

namespace GSKit.CLI.Commands;

/// <summary>
/// gskit info &lt;sku&gt; [--condition Pre-Owned|New] [--platform &lt;platform&gt;]
///
/// Fetches product title, price (regular + Pro), availability, and trade-in
/// value from Product-Variation.
/// </summary>
public static class InfoCommand
{
    public static async Task<int> RunAsync(string[] args, RunContext ctx)
    {
        string? sku       = null;
        string  condition = "Pre-Owned";
        string  platform  = "";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--condition": condition = args[++i]; break;
                case "--platform":  platform  = args[++i]; break;
                default:
                    if (sku is null && !args[i].StartsWith('-'))
                        sku = args[i];
                    break;
            }
        }

        if (sku is null)
        {
            Log.Err("SKU required  →  gskit info <sku>");
            return 1;
        }

        Log.Http($"Product-Variation  sku={sku}  condition={condition}");

        ProductInfo? info;
        var sw = Stopwatch.StartNew();
        try
        {
            await using var client = new GameStopClient(ctx.Debug);
            var scraper = new ProductScraper(client);
            info = await scraper.GetProductInfoAsync(sku, condition, platform);
        }
        catch (Exception ex)
        {
            Log.Err(ex.Message);
            if (ctx.Debug) AnsiConsole.WriteException(ex);
            return 1;
        }

        sw.Stop();

        if (info is null)
        {
            Log.Warn("no product data returned for this SKU");
            return 1;
        }

        Log.Ok($"fetched in {sw.ElapsedMilliseconds}ms");
        AnsiConsole.WriteLine();

        PrintInfo(info);
        return 0;
    }

    private static void PrintInfo(ProductInfo p)
    {
        AnsiConsole.Write(new Rule($"[white bold]{Markup.Escape(p.Title)}[/]")
            { Justification = Justify.Left });
        AnsiConsole.WriteLine();

        var grid = new Grid()
            .AddColumn(new GridColumn().Width(22))
            .AddColumn(new GridColumn());

        void Row(string label, string value, string color = "grey85") =>
            grid.AddRow($"[dim]{label}[/]", $"[{color}]{Markup.Escape(value)}[/]");

        Row("SKU",          p.Sku);
        Row("Condition",    p.Condition);
        if (p.Platform is { Length: > 0 }) Row("Platform", p.Platform);
        Row("Price",        p.PriceFormatted,          "green bold");
        if (p.ProPriceFormatted is { Length: > 0 } && p.ProPriceFormatted != p.PriceFormatted)
            Row("Pro Price",    p.ProPriceFormatted,   "cyan");
        Row("Availability", p.AvailabilityMessage,     p.Available ? "green" : "yellow");
        if (p.TradeBasePrice > 0)
            Row("Trade-in value", $"${p.TradeBasePrice:F2}",  "yellow");
        if (p.IsTradeable) Row("Tradeable",   "yes", "dim");
        if (p.IsPreorder)  Row("Pre-order",   "yes", "yellow");
        if (p.IsBackorder) Row("Backorder",   "yes", "yellow");
        if (p.Publisher is { Length: > 0 })   Row("Publisher",  p.Publisher);
        if (p.Developer is { Length: > 0 })   Row("Developer",  p.Developer);
        if (p.Genre is { Length: > 0 })       Row("Genre",      p.Genre);
        if (p.ReleaseDate is { Length: > 0 }) Row("Release date", p.ReleaseDate);

        AnsiConsole.Write(grid);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule() { Style = Style.Parse("dim"), Justification = Justify.Left });
        AnsiConsole.MarkupLine($"[dim]  sku {p.Sku}  ·  gamestop.com[/]");
        AnsiConsole.WriteLine();
    }
}
