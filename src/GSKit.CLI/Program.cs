using GSKit.CLI.Commands;
using Spectre.Console;

var args2 = args.ToList();
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
    "stock" => await StockCommand.RunAsync(args2.Skip(1).ToArray(), debug),
    _       => UnknownCommand(args2[0]),
};

static void PrintHelp()
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold]gskit[/]") { Justification = Justify.Left });
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("  [bold]stock[/] [dim]<sku> [OPTIONS][/]"
        .Replace("[OPTIONS]", "[[OPTIONS]]"));
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("  [dim]--zip <code>           search from US zip code[/]");
    AnsiConsole.MarkupLine("  [dim]--lat / --long         coordinates (skips geocode)[/]");
    AnsiConsole.MarkupLine("  [dim]--radius <miles>       search radius  (default: 100)[/]");
    AnsiConsole.MarkupLine("  [dim]--in-stock-only        hide out-of-stock stores[/]");
    AnsiConsole.MarkupLine("  [dim]--format table|json    output format  (default: table)[/]");
    AnsiConsole.MarkupLine("  [dim]--debug                dump raw JSON response[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("  [dim]gskit stock 133857 --zip <zip>[/]");
    AnsiConsole.MarkupLine("  [dim]gskit stock 133857 --zip <zip> --in-stock-only[/]");
    AnsiConsole.MarkupLine("  [dim]gskit stock 133857 --lat <lat> --long <lon> --format json[/]");
    AnsiConsole.WriteLine();
}

static int UnknownCommand(string cmd)
{
    AnsiConsole.MarkupLine($"[red][[ERROR]][/] unknown command: {cmd}  (try --help)");
    return 1;
}
