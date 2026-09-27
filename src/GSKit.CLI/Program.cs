using GSKit.CLI;
using GSKit.CLI.Commands;
using GSKit.CLI.Output;
using Spectre.Console;

var argList = args.ToList();
bool debug   = argList.Remove("--debug");
bool noColor = argList.Remove("--no-color");
bool verbose = argList.Remove("--verbose") || argList.Remove("-v");

int fmtIdx = argList.IndexOf("--format");
bool jsonMode = fmtIdx >= 0 && fmtIdx + 1 < argList.Count &&
                argList[fmtIdx + 1].Equals("json", StringComparison.OrdinalIgnoreCase);

if (noColor) AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
if (jsonMode) Log.Quiet = true;

if (argList.Count == 0 || argList[0] is "-h" or "--help")
{
    HelpPrinter.Print();
    return 0;
}

if (argList[0] is "--version" or "-V")
{
    AnsiConsole.MarkupLine("[grey85]gskit[/] [dim]0.2.0[/]");
    return 0;
}

var cfg = GsConfig.Load();

if (cfg.DefaultZip is { Length: > 0 })
{
    bool hasZip = argList.Contains("--zip") || argList.Contains("--lat");
    if (!hasZip && argList.Count > 0 && argList[0] is "stock" or "sdd")
    {
        argList.Add("--zip");
        argList.Add(cfg.DefaultZip);
        Log.Dbg("config: defaultZip applied", debug);
    }
}

if (cfg.DefaultRadius.HasValue)
{
    bool hasRadius = argList.Contains("--radius");
    if (!hasRadius && argList.Count > 0 && argList[0] is "stock")
    {
        argList.Add("--radius");
        argList.Add(cfg.DefaultRadius.Value.ToString("F0"));
        Log.Dbg($"config: defaultRadius={cfg.DefaultRadius:F0}", debug);
    }
}

var ctx = new RunContext(debug, verbose, jsonMode);

return argList[0] switch
{
    "stock"              => await StockCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    "info"               => await InfoCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    "sdd"                => await SddCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    "probe"              => await ProbeCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    "search"             => await SearchCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    "tiles"              => await TilesCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    "store-availability" => await StoreAvailabilityCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    "trade-search"       => await TradeSearchCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    "trade-value"        => await TradeValueCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    _                    => Unknown(argList[0]),
};

static int Unknown(string cmd)
{
    Log.Err($"unknown command '{cmd}'  —  try --help");
    return 1;
}
