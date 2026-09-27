using Spectre.Console;
using GSKit.Core.Http;
using GSKit.Core.Models;
using GSKit.CLI.Output;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace GSKit.CLI.Commands;

/// <summary>
/// gskit search &lt;query&gt; [--platform &lt;p&gt;] [--condition &lt;c&gt;] [--page N] [--results N]
///
/// Searches GameStop's catalog via Constructor.io (ac.cnstrc.com).
/// API key key_FIW9YAimY77z5QEf is baked into gamestop's constructorClient.js — public.
/// Works from any IP — separate domain, no Cloudflare.
/// </summary>
public static class SearchCommand
{
    public static async Task<int> RunAsync(string[] args, RunContext ctx)
    {
        string? query    = null;
        string? platform = null;
        string? cond     = null;
        int     page     = 1;
        int     results  = 10;
        string  format   = "table";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--platform":  platform = args[++i]; break;
                case "--condition": cond     = args[++i]; break;
                case "--page":      page     = int.Parse(args[++i]); break;
                case "--results":   results  = int.Parse(args[++i]); break;
                case "--format":    format   = args[++i]; break;
                default:
                    if (query is null && !args[i].StartsWith('-'))
                        query = args[i];
                    else if (query is not null && !args[i].StartsWith('-'))
                        query += " " + args[i];
                    break;
            }
        }

        if (query is null)
        {
            Log.Err("query required  →  gskit search <query>");
            return 1;
        }

        Log.Http($"Constructor-Search  q=\"{query}\"" +
                 (platform is not null ? $"  platform={platform}" : "") +
                 (cond     is not null ? $"  condition={cond}" : "") +
                 $"  page={page}  results={results}");

        var sw = Stopwatch.StartNew();
        List<SearchResult> items;
        int total;

        try
        {
            await using var client = new ConstructorClient(ctx.Debug);
            (items, total) = await client.SearchAsync(query, page, results, platform, cond);
        }
        catch (Exception ex)
        {
            Log.Err(ex.Message);
            return 1;
        }

        sw.Stop();

        var shown = items.Count;
        Log.Ok($"{total} total  ·  showing {shown}  [[{sw.ElapsedMilliseconds}ms]]");
        AnsiConsole.WriteLine();

        return format.ToLower() switch
        {
            "json" => OutputJson(items, total),
            _      => OutputTable(items, total, page, results),
        };
    }

    private static int OutputTable(List<SearchResult> items, int total, int page, int perPage)
    {
        if (items.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]  no results[/]");
            AnsiConsole.WriteLine();
            return 0;
        }

        var t = new Table()
            .BorderStyle(Style.Parse("grey23"))
            .Border(TableBorder.Simple)
            .AddColumn(new TableColumn("[dim]SKU[/]"))
            .AddColumn(new TableColumn("[dim]NAME[/]"))
            .AddColumn(new TableColumn("[dim]PLATFORM[/]"))
            .AddColumn(new TableColumn("[dim]COND[/]"))
            .AddColumn(new TableColumn("[dim]PRICE[/]") { Alignment = Justify.Right })
            .AddColumn(new TableColumn("[dim]VAR[/]")   { Alignment = Justify.Right });

        foreach (var r in items)
        {
            var name = r.Name.Length > 48 ? r.Name[..45] + "…" : r.Name;
            var plat = r.Platform.Length > 18 ? r.Platform[..15] + "…" : r.Platform;
            t.AddRow(
                $"[dim]{Markup.Escape(r.Sku)}[/]",
                Markup.Escape(name),
                $"[dim]{Markup.Escape(plat)}[/]",
                r.Condition == "Pre-Owned" ? "[yellow]Pre-Owned[/]" : Markup.Escape(r.Condition),
                r.Price > 0 ? $"[green]${r.Price:F2}[/]" : "[dim]—[/]",
                r.VariationCount > 1 ? $"[dim]{r.VariationCount}[/]" : "[dim]1[/]"
            );
        }

        AnsiConsole.Write(t);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule() { Justification = Justify.Left, Style = Style.Parse("dim") });

        int offset = (page - 1) * perPage;
        AnsiConsole.MarkupLine(
            $"[dim]  {offset + 1}–{offset + items.Count} of {total}  ·  Constructor-Search  ·  use --page to paginate[/]");
        AnsiConsole.WriteLine();

        return 0;
    }

    private static int OutputJson(List<SearchResult> items, int total)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            total,
            results = items.Select(r => new
            {
                sku            = r.Sku,
                name           = r.Name,
                platform       = r.Platform,
                condition      = r.Condition,
                edition        = r.Edition,
                price          = r.Price,
                brand          = r.Brand,
                url            = r.Url,
                variation_count = r.VariationCount,
            }),
        }, opts));
        return 0;
    }
}
