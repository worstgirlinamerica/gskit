using Spectre.Console;
using GSKit.Core.Http;
using GSKit.Core.Scrapers;
using GSKit.CLI.Output;
using System.Diagnostics;
using System.Text.Json;

namespace GSKit.CLI.Commands;

/// <summary>
/// gskit trade-value &lt;productId&gt; [--condition Pre-Owned|New] [--format table|json] [--raw]
///
/// Hits Trade-Show — the server-rendered HTML partial that GameStop's trade wizard
/// displays after you select a product. Returns the full trade value breakdown:
///   • Cash value    — what they pay you in cash
///   • Credit value  — what they pay as in-store credit (always higher than cash)
///   • Pro bonus     — additional credit for Pro members on top of credit value
///
/// The productId must come from `gskit trade-search` — it is NOT the same as the
/// SKU used in other gskit commands (GameStop uses a separate ID in their trade system).
///
/// --raw prints the raw HTML response for debugging / confirming selector accuracy.
///
/// NOTE: residential IP only (CF blocks datacenter).
/// </summary>
public static class TradeValueCommand
{
    public static async Task<int> RunAsync(string[] args, RunContext ctx)
    {
        string? productId = null;
        string  condition = "Pre-Owned";
        string  format    = "table";
        bool    raw       = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--condition": condition = args[++i]; break;
                case "--format":   format    = args[++i]; break;
                case "--raw":      raw       = true;       break;
                default:
                    if (productId is null && !args[i].StartsWith('-'))
                        productId = args[i];
                    break;
            }
        }

        if (productId is null)
        {
            Log.Err("productId required  →  gskit trade-value <productId>");
            Log.Err("get productId from:  gskit trade-search <query>");
            return 1;
        }

        Log.Http($"Trade-Show  pid={productId}  condition={condition}");

        var sw = Stopwatch.StartNew();
        GSKit.Core.Models.TradeValue? value;
        try
        {
            await using var client  = new GameStopClient(ctx.Debug);
            var             scraper = new TradeScraper(client);
            value = await scraper.GetTradeValueAsync(productId, condition);
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
        Log.Ok($"fetched  [{sw.ElapsedMilliseconds}ms]");
        AnsiConsole.WriteLine();

        if (value is null)
        {
            Log.Warn("no trade value returned");
            return 1;
        }

        if (raw)
        {
            AnsiConsole.WriteLine(value.RawHtml);
            return 0;
        }

        if (format == "json")
        {
            var obj = new
            {
                value.ProductId, value.Condition, value.ProductName,
                value.CashValue, value.CreditValue, value.ProBonusValue
            };
            AnsiConsole.WriteLine(JsonSerializer.Serialize(obj,
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        PrintValue(value);
        return 0;
    }

    private static void PrintValue(GSKit.Core.Models.TradeValue v)
    {
        var title = v.ProductName.Length > 0 ? v.ProductName : $"Product {v.ProductId}";
        AnsiConsole.Write(new Rule($"[white bold]{Markup.Escape(title)}[/]")
            { Justification = Justify.Left });
        AnsiConsole.WriteLine();

        var grid = new Grid()
            .AddColumn(new GridColumn().Width(22))
            .AddColumn(new GridColumn());

        void Row(string label, string val, string color = "grey85") =>
            grid.AddRow($"[dim]{label}[/]", $"[{color}]{Markup.Escape(val)}[/]");

        Row("Product ID",    v.ProductId);
        Row("Condition",     v.Condition);
        AnsiConsole.Write(grid);
        AnsiConsole.WriteLine();

        // Value breakdown panel
        var valGrid = new Grid()
            .AddColumn(new GridColumn().Width(22))
            .AddColumn(new GridColumn());

        void ValRow(string label, decimal amount, string color) =>
            valGrid.AddRow($"[dim]{label}[/]",
                amount > 0
                    ? $"[{color} bold]${amount:F2}[/]"
                    : "[dim]—[/]");

        ValRow("Cash value",     v.CashValue,     "yellow");
        ValRow("In-store credit",v.CreditValue,   "green");
        if (v.ProBonusValue > 0)
            ValRow("Pro bonus (+)",  v.ProBonusValue, "cyan");
        if (v.ProBonusValue > 0 && v.CreditValue > 0)
            ValRow("Pro total",      v.CreditValue + v.ProBonusValue, "cyan bold");

        AnsiConsole.Write(valGrid);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule() { Style = Style.Parse("dim"), Justification = Justify.Left });
        AnsiConsole.MarkupLine(
            v.CashValue == 0 && v.CreditValue == 0
                ? "[yellow dim]  ⚠  values not parsed — run with --raw to inspect response HTML[/]"
                : $"[dim]  pid {v.ProductId}  ·  Trade-Show  ·  Trade-GetSuggestions for lookup[/]");
        AnsiConsole.WriteLine();
    }
}
