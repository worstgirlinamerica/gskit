using Spectre.Console;
using GSKit.Core.Http;
using GSKit.Core.Scrapers.Inventory;
using GSKit.CLI.Output;
using System.Diagnostics;

namespace GSKit.CLI.Commands;

/// <summary>
/// gskit probe &lt;sku&gt; --store &lt;storeId&gt; --zip &lt;zip&gt;
///
/// Dev/debug command. Hits Stores-FindStores?selectedStore=&lt;id&gt; and prints the
/// raw preferredStore inventory[] for that store. The SetPreferredStore POST
/// (Stores-UpdateInStoreShipmentID) was removed — it requires a dwsid session
/// cookie to work and 403s without one. The selectedStore query param alone is
/// enough to get the preferredStore inventory block.
/// </summary>
public static class ProbeCommand
{
    public static async Task<int> RunAsync(string[] args, RunContext ctx)
    {
        string? sku     = null;
        string? storeId = null;
        string? zip     = null;
        double? lat     = null;
        double? lon     = null;
        double  radius  = 100.0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--store":  storeId = args[++i]; break;
                case "--zip":    zip     = args[++i]; break;
                case "--lat":    lat     = double.Parse(args[++i]); break;
                case "--long":   lon     = double.Parse(args[++i]); break;
                case "--radius": radius  = double.Parse(args[++i]); break;
                default:
                    if (sku is null && !args[i].StartsWith('-')) sku = args[i];
                    break;
            }
        }

        if (sku is null || storeId is null)
        {
            Log.Err("usage: gskit probe <sku> --store <storeId> --zip <zip>");
            return 1;
        }

        double searchLat, searchLon;
        if (lat.HasValue && lon.HasValue)
        {
            searchLat = lat.Value;
            searchLon = lon.Value;
        }
        else if (zip is not null)
        {
            Log.Geo($"resolving {zip}");
            (searchLat, searchLon) = await GeocodingHelper.ZipToLatLonAsync(zip);
        }
        else
        {
            Log.Err("provide --zip or --lat/--long");
            return 1;
        }

        await using var client = new GameStopClient(ctx.Debug);

        Log.Http($"Stores-FindStores  sku={sku}  selectedStore={storeId}");
        var sw      = Stopwatch.StartNew();
        var scraper = new StoreInventory(client);
        var raw     = await scraper.ProbeStoreInventoryAsync(sku, storeId, searchLat, searchLon, radius);
        sw.Stop();

        Log.Ok($"done  [[{sw.ElapsedMilliseconds}ms]]");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine(raw);
        return 0;
    }
}
