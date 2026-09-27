using GSKit.CLI;
using GSKit.CLI.Commands;
using GSKit.CLI.Output;
using Spectre.Console;

var argList = args.ToList();
bool debug   = argList.Remove("--debug");
bool noColor = argList.Remove("--no-color");
bool verbose = argList.Remove("--verbose") || argList.Remove("-v");

if (noColor) AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;

if (argList.Count == 0 || argList[0] is "-h" or "--help")
{
    HelpPrinter.Print();
    return 0;
}

if (argList[0] is "--version" or "-V")
{
    AnsiConsole.MarkupLine("[grey85]gskit[/] [dim]0.1.0[/]");
    return 0;
}

// Load user config and inject defaults where the caller didn't supply them
var cfg = GsConfig.Load();

if (cfg.DefaultZip is { Length: > 0 })
{
    // Inject --zip default if stock/sdd command didn't provide one
    bool hasZip = argList.Contains("--zip") || argList.Contains("--lat");
    if (!hasZip && argList.Count > 0 && argList[0] is "stock" or "sdd")
    {
        argList.Add("--zip");
        argList.Add(cfg.DefaultZip);
        Log.Dbg($"config: defaultZip={cfg.DefaultZip}", debug);
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

var ctx = new RunContext(debug, verbose);

return argList[0] switch
{
    "stock" => await StockCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    "info"  => await InfoCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    "sdd"   => await SddCommand.RunAsync(argList.Skip(1).ToArray(), ctx),
    _       => Unknown(argList[0]),
};

static int Unknown(string cmd)
{
    Log.Err($"unknown command '{cmd}'  —  try --help");
    return 1;
}
