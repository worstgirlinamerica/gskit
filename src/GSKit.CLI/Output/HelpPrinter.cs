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
        Cmd("gskit stock <sku> [OPTIONS]",   "locate inventory near a zip code");
        Cmd("gskit info  <sku> [OPTIONS]",   "fetch product info: title, price, availability");
        Cmd("gskit sdd   <sku>",             "check same-day delivery eligibility");
        AnsiConsole.WriteLine();

        Section("LOCATION OPTIONS  (stock)");
        Opt("--zip <code>",        "resolve from US zip code (uses census geocoder)");
        Opt("--lat / --long <n>",  "raw coordinates — skips geocoding");
        Opt("--radius <miles>",    "search radius, default 100");
        AnsiConsole.WriteLine();

        Section("OUTPUT OPTIONS");
        Opt("--in-stock-only",     "hide out-of-stock stores");
        Opt("--verbose, -v",       "show address + phone on each store row");
        Opt("--format table|json", "output format, default table");
        Opt("--no-color",          "disable ANSI color (pipe / log-file safe)");
        Opt("--debug",             "print raw HTTP requests, responses, and JSON");
        Opt("--version",           "print version and exit");
        AnsiConsole.WriteLine();

        Section("CONFIG");
        AnsiConsole.MarkupLine("  [dim]~/.config/gskit/config.json[/]");
        AnsiConsole.MarkupLine("  [dim]  {[/]");
        AnsiConsole.MarkupLine("  [dim]    \"defaultZip\":    \"<your zip>\",  [/]");
        AnsiConsole.MarkupLine("  [dim]    \"defaultRadius\": 100[/]");
        AnsiConsole.MarkupLine("  [dim]  }[/]");
        AnsiConsole.WriteLine();

        Section("EXAMPLES");
        Ex("gskit stock <sku> --zip <zip>");
        Ex("gskit stock <sku> --zip <zip> --in-stock-only -v");
        Ex("gskit stock <sku> --zip <zip> --format json");
        Ex("gskit stock <sku> --lat <lat> --long <lon>");
        Ex("gskit info  <sku>");
        Ex("gskit info  <sku> --debug");
        Ex("gskit sdd   <sku>");
        AnsiConsole.WriteLine();

        Section("NOTE");
        AnsiConsole.MarkupLine("  [dim]When using dotnet run, pass -- before gskit args:[/]");
        AnsiConsole.MarkupLine("  [dim]  dotnet run --project src/GSKit.CLI -- stock <sku> --zip <zip>[/]");
        AnsiConsole.MarkupLine("  [dim]  dotnet run --project src/GSKit.CLI -- --help[/]");
        AnsiConsole.WriteLine();
    }

    private static void Section(string name) =>
        AnsiConsole.MarkupLine($"[bold grey85]{name}[/]");

    private static void Cmd(string usage, string desc) =>
        AnsiConsole.MarkupLine($"  [cyan]{Markup.Escape(usage),-36}[/] [dim]{Markup.Escape(desc)}[/]");

    private static void Opt(string flag, string desc) =>
        AnsiConsole.MarkupLine($"  [grey85]{Markup.Escape(flag),-26}[/] [dim]{Markup.Escape(desc)}[/]");

    private static void Ex(string cmd) =>
        AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(cmd)}[/]");
}
