using Spectre.Console;
using GSKit.Core.Http;
using GSKit.Core.Models;
using GSKit.Core.Scrapers;
using GSKit.CLI.Output;
using System.Diagnostics;

namespace GSKit.CLI.Commands;

/// <summary>
/// gskit sdd &lt;sku&gt;
///
/// Checks Product-SameDayDelivery: whether SDD is offered near the user's
/// detected location, and if the order cutoff has passed.
/// </summary>
public static class SddCommand
{
    public static async Task<int> RunAsync(string[] args, RunContext ctx)
    {
        string? sku = null;
        for (int i = 0; i < args.Length; i++)
            if (sku is null && !args[i].StartsWith('-')) sku = args[i];

        if (sku is null)
        {
            Log.Err("SKU required  →  gskit sdd <sku>");
            return 1;
        }

        Log.Http($"Product-SameDayDelivery  sku={sku}");

        SameDayResult? sdd;
        var sw = Stopwatch.StartNew();
        try
        {
            await using var client = new GameStopClient(ctx.Debug);
            var scraper = new ProductScraper(client);
            sdd = await scraper.GetSameDayDeliveryAsync(sku);
        }
        catch (Exception ex)
        {
            Log.Err(ex.Message);
            if (ctx.Debug) AnsiConsole.WriteException(ex);
            return 1;
        }

        sw.Stop();

        if (sdd is null)
        {
            Log.Warn("no SDD data returned");
            return 1;
        }

        Log.Ok($"fetched in {sw.ElapsedMilliseconds}ms");
        AnsiConsole.WriteLine();
        PrintSdd(sku, sdd);
        return 0;
    }

    private static void PrintSdd(string sku, SameDayResult s)
    {
        AnsiConsole.Write(new Rule("[white bold]Same-Day Delivery[/]") { Justification = Justify.Left });
        AnsiConsole.WriteLine();

        var grid = new Grid()
            .AddColumn(new GridColumn().Width(26))
            .AddColumn(new GridColumn());

        void Row(string label, string val, string color = "grey85") =>
            grid.AddRow($"[dim]{label}[/]", $"[{color}]{Markup.Escape(val)}[/]");

        Row("SKU",               sku);
        Row("SDD hidden",        s.HideSdd     ? "yes (ineligible)" : "no",  s.HideSdd   ? "dim"    : "green");
        Row("Nearby stock",      s.NearBy      ? "yes"              : "no",  s.NearBy    ? "green"  : "yellow");
        Row("Product available", s.ProductAvail? "yes"              : "no",  s.ProductAvail?"green" : "dim");
        Row("Cutoff passed",     s.IsTimeCutOff? "yes — too late today" : "no", s.IsTimeCutOff ? "yellow" : "green");
        if (s.TimeLeft is { Length: > 0 }) Row("Time left to order", s.TimeLeft, "cyan");
        Row("ATC disabled",      s.AtcDisable  ? "yes" : "no",  s.AtcDisable  ? "yellow" : "dim");

        AnsiConsole.Write(grid);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule() { Style = Style.Parse("dim"), Justification = Justify.Left });
        AnsiConsole.MarkupLine($"[dim]  sku {sku}  ·  Product-SameDayDelivery[/]");
        AnsiConsole.WriteLine();
    }
}
