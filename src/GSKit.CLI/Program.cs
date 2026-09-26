using System.CommandLine;
using GSKit.CLI.Commands;

var root = new RootCommand("GSKit — GameStop reverse engineering toolkit");

root.AddCommand(StockCommand.Build());
// root.AddCommand(SearchCommand.Build());   // Phase 2
// root.AddCommand(TradeCommand.Build());    // Phase 3
// root.AddCommand(WatchCommand.Build());    // Phase 4
// root.AddCommand(ReconCommand.Build());    // Phase 5
// root.AddCommand(AuthCommand.Build());     // Phase 6

return await root.InvokeAsync(args);
