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
