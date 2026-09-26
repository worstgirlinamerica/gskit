using GSKit.CLI.Commands;
using Spectre.Console;

// Top-level dispatch — lean, all logic lives in command files
var args2 = args.ToList();

// Pull global flags before dispatch
bool debug   = args2.Remove("--debug");
bool noColor = args2.Remove("--no-color");

if (noColor) AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;

if (args2.Count == 0 || args2[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

return args2[0] switch
{
    "stock"  => await StockCommand.RunAsync(args2.Skip(1).ToArray(), debug),
    _        => UnknownCommand(args2[0]),
};

static void PrintHelp()
{
    AnsiConsole.MarkupLine("[bold yellow]gskit[/] — GameStop reverse engineering toolkit\n");
    AnsiConsole.MarkupLine("  [bold]gskit stock[/] [grey]<sku> [[OPTIONS]][/]   Check in-store inventory\n");
    AnsiConsole.MarkupLine("[grey]Options (stock):[/]");
    AnsiConsole.MarkupLine("  [grey]--zip <code>           US zip to search from[/]");
    AnsiConsole.MarkupLine("  [grey]--lat / --long         Coords instead of zip (faster)[/]");
    AnsiConsole.MarkupLine("  [grey]--radius <miles>       Search radius (default: 100)[/]");
    AnsiConsole.MarkupLine("  [grey]--in-stock-only        Hide out-of-stock stores[/]");
    AnsiConsole.MarkupLine("  [grey]--format table|json    Output format (default: table)[/]");
    AnsiConsole.MarkupLine("  [grey]--session 'k=v;k=v'   Manual cookies (skips Chrome read)[/]");
    AnsiConsole.MarkupLine("  [grey]--debug                Raw JSON + verbose HTTP details[/]\n");
    AnsiConsole.MarkupLine("[grey]Examples:[/]");
    AnsiConsole.MarkupLine("  [grey]gskit stock 133857 --zip <zip> --radius 150[/]");
    AnsiConsole.MarkupLine("  [grey]gskit stock 133857 --lat <lat> --long <lon>[/]");
    AnsiConsole.MarkupLine("  [grey]gskit stock 133857 --zip 10001 --radius 50 --in-stock-only[/]");
    AnsiConsole.MarkupLine("  [grey]gskit stock 133857 --zip <zip> --format json[/]");
    AnsiConsole.MarkupLine("  [grey]gskit stock 133857 --zip <zip> --debug[/]");
}

static int UnknownCommand(string cmd)
{
    AnsiConsole.MarkupLine($"[red]Unknown command: {cmd}[/]  (try --help)");
    return 1;
}
