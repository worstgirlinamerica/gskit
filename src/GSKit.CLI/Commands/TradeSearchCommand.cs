using Spectre.Console;
using GSKit.Core.Http;
using GSKit.Core.Scrapers;
using GSKit.CLI.Output;
using System.Diagnostics;
using System.Text.Json;

namespace GSKit.CLI.Commands;

/// <summary>
/// gskit trade-search &lt;query&gt; [--format table|json]
///
/// Hits Trade-GetSuggestions — the autocomplete search powering GameStop's
/// trade-in wizard. Returns product matches with their internal trade productId.
///
/// The productId returned here is what you pass to `gskit trade-value` to get
/// cash and credit amounts. It is NOT the same as the SKU used in other commands.
///
/// NOTE: residential IP only (CF blocks datacenter).
/// </summary>
public static class TradeSearchCommand
{
    public static async Task<int> RunAsync(string[] args, RunContext ctx)
    {
        string? query  = null;
        string  format = "table";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--format": format = args[++i]; break;
                default:
                    if (query is null && !args[i].StartsWith('-'))
                        query = args[i];
                    break;
            }
        }

        if (query is null)
        {
            Log.Err("query required  →  gskit trade-search <query>");
            return 1;
        }

        Log.Http($"Trade-GetSuggestions  q=\"{query}\"");

        var sw = Stopwatch.StartNew();
        List<GSKit.Core.Models.TradeSuggestion> results;
        try
        {
            await using var client  = new GameStopClient(ctx.Debug);
            var             scraper = new TradeScraper(client);
            results = await scraper.GetSuggestionsAsync(query);
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
        Log.Ok($"{results.Count} result{(results.Count == 1 ? "" : "s")}  [{sw.ElapsedMilliseconds}ms]");
        AnsiConsole.WriteLine();

        if (format == "json")
        {
            AnsiConsole.WriteLine(JsonSerializer.Serialize(results,
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        if (results.Count == 0)
        {
            Log.Warn("no trade matches for that query");
            return 0;
        }

        var table = new Table()
            .AddColumn(new TableColumn("[dim]PRODUCT ID[/]"))
            .AddColumn(new TableColumn("[dim]NAME[/]"))
            .BorderStyle(Style.Parse("dim"))
            .Border(TableBorder.Simple);

        foreach (var r in results)
        {
            table.AddRow(
                Markup.Escape(r.ProductId),
                Markup.Escape(r.Name)
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]  Trade-GetSuggestions  ·  use productId with gskit trade-value[/]");
        AnsiConsole.WriteLine();
        return 0;
    }
}
