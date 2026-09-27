using Spectre.Console;

namespace GSKit.CLI.Output;

public static class HelpPrinter
{
    public static void Print()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold white]gskit[/] [dim]— GameStop store-inventory toolkit[/]");
        AnsiConsole.WriteLine();

        Section("USAGE");
        Cmd("gskit stock <sku> [OPTIONS]",    "locate inventory near a zip code");
        Cmd("gskit info  <sku> [OPTIONS]",    "fetch product info: title, price, availability");
        Cmd("gskit sdd   <sku>",              "check same-day delivery eligibility");
        AnsiConsole.WriteLine();

        Section("LOCATION OPTIONS");
        Opt("--zip <code>",          "resolve from US zip code (uses census geocoder)");
        Opt("--lat / --long <n>",    "raw coordinates — skips geocoding");
        Opt("--radius <miles>",      "search radius, default 100");
        AnsiConsole.WriteLine();

        Section("OUTPUT OPTIONS");
        Opt("--in-stock-only",       "hide out-of-stock stores");
        Opt("--verbose, -v",         "show address + phone on each store row");
        Opt("--format table|json",   "output format, default table");
        Opt("--no-color",            "disable ANSI color (pipe / log-file safe)");
        Opt("--debug",               "print raw JSON responses and timing");
        AnsiConsole.WriteLine();

        Section("EXAMPLES");
        AnsiConsole.MarkupLine("  [dim]gskit stock 133857 --zip <zip>[/]");
        AnsiConsole.MarkupLine("  [dim]gskit stock 133857 --zip <zip> --in-stock-only -v[/]");
        AnsiConsole.MarkupLine("  [dim]gskit stock 133857 --lat <lat> --long <lon> --format json[/]");
        AnsiConsole.MarkupLine("  [dim]gskit info  133857[/]");
        AnsiConsole.MarkupLine("  [dim]gskit sdd   133857[/]");
        AnsiConsole.MarkupLine("  [dim]gskit stock 133857 --zip <zip> --debug[/]");
        AnsiConsole.WriteLine();
    }

    private static void Section(string name) =>
        AnsiConsole.MarkupLine($"[bold grey85]{name}[/]");

    private static void Cmd(string usage, string desc) =>
        AnsiConsole.MarkupLine($"  [cyan]{Markup.Escape(usage)}[/]");

    private static void Opt(string flag, string desc) =>
        AnsiConsole.MarkupLine($"  [grey85]{Markup.Escape(flag),-28}[/] [dim]{Markup.Escape(desc)}[/]");
}
