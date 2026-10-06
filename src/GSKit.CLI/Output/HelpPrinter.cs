using Spectre.Console;

namespace GSKit.CLI.Output;

public static class HelpPrinter
{
    public static void Print()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold white]gskit[/] [dim]— GameStop toolkit[/]");
        AnsiConsole.WriteLine();

        Section("USAGE");
        Cmd("gskit search <query>",          "search catalog via Constructor-Search (ac.cnstrc.com)");
        Cmd("gskit stock <sku>",             "locate inventory near a zip code  (Stores-FindStores)");
        Cmd("gskit tiles <sku> [sku2 ...]",  "batch price + availability lookup  (Tile-GetProductsJSON)");
        Cmd("gskit store-availability <sku>","per-variant availability at preferred store");
        Cmd("gskit trade-search <query>",   "trade wizard product search  (Trade-GetSuggestions)");
        Cmd("gskit trade-value <productId>", "cash + credit trade values  (Trade-Show)");
        Cmd("gskit info <sku>",              "product title, price, trade-in     (Product-Variation)");
        Cmd("gskit sdd <sku>",               "same-day delivery eligibility      (Product-SameDayDelivery)");
        Cmd("gskit probe <sku>",             "dev: raw inventory probe via selectedStore");
        AnsiConsole.WriteLine();

        Section("SEARCH OPTIONS");
        Opt("--platform <name>",   "filter by platform, e.g. \"Xbox One\", \"PlayStation 5\"");
        Opt("--condition <name>",  "filter by condition, e.g. \"Pre-Owned\", \"New\"");
        Opt("--results N",         "results per page, default 10");
        Opt("--page N",            "page number, default 1");
        AnsiConsole.WriteLine();

        Section("STOCK / LOCATION OPTIONS");
        Opt("--zip <code>",        "resolve from US zip code (census geocoder)");
        Opt("--lat / --long <n>",  "raw coordinates — skips geocoding");
        Opt("--radius <miles>",    "search radius, default 100");
        Opt("--in-stock-only",     "hide out-of-stock stores");
        AnsiConsole.WriteLine();

        Section("OUTPUT OPTIONS  (all commands)");
        Opt("--format table|json", "output format, default table");
        Opt("--verbose, -v",       "show extra columns (address, phone)");
        Opt("--no-color",          "disable ANSI color");
        Opt("--debug",             "print raw HTTP details");
        Opt("--version",           "print version and exit");
        AnsiConsole.WriteLine();

        Section("CONFIG  (~/.config/gskit/config.json)");
        AnsiConsole.MarkupLine("  [dim]{ \"defaultZip\": \"<zip>\", \"defaultRadius\": 100 }[/]");
        AnsiConsole.WriteLine();

        Section("EXAMPLES");
        Ex("gskit search \"call of duty\" --platform \"Xbox One\" --condition Pre-Owned");
        Ex("gskit stock <sku> --zip <zip>");
        Ex("gskit stock <sku> --zip <zip> --in-stock-only --format json");
        Ex("gskit tiles <sku1> <sku2> <sku3>");
        Ex("gskit store-availability <sku>");
        Ex("gskit trade-search \"call of duty infinite warfare\"");
        Ex("gskit trade-value <productId> --condition Pre-Owned");
        Ex("gskit trade-value <productId> --raw  # inspect raw HTML when values are zero");
        Ex("gskit info <sku> --debug");
        Ex("gskit sdd <sku>");
        AnsiConsole.WriteLine();

        Section("NOTES");
        AnsiConsole.MarkupLine(
            "  [dim]search  — Constructor.io API, works from any IP (no Cloudflare)[/]");
        AnsiConsole.MarkupLine(
            "  [dim]stock   — Stores-FindStores, works from residential IP[/]");
        AnsiConsole.MarkupLine(
            "  [dim]tiles / store-availability / trade-search / trade-value — residential only[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            "  [dim]dotnet run:  dotnet run --project src/GSKit.CLI -- <command> [[args]][/]");
        AnsiConsole.WriteLine();
    }

    private static void Section(string name) =>
        AnsiConsole.MarkupLine($"[bold grey85]{name}[/]");

    private static void Cmd(string usage, string desc) =>
        AnsiConsole.MarkupLine($"  [cyan]{Markup.Escape(usage),-40}[/] [dim]{Markup.Escape(desc)}[/]");

    private static void Opt(string flag, string desc) =>
        AnsiConsole.MarkupLine($"  [grey85]{Markup.Escape(flag),-24}[/] [dim]{Markup.Escape(desc)}[/]");

    private static void Ex(string cmd) =>
        AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(cmd)}[/]");
}
