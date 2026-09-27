# gskit

GameStop reverse engineering toolkit. Inventory, trade-ins, pricing, store data.

## Install

Requires [.NET SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/worstgirlinamerica/gskit
cd gskit
dotnet build
```

## Usage

```bash
# Check in-store inventory by zip code
dotnet run --project src/GSKit.CLI -- stock <sku> --zip <zip>

# By coordinates (faster, skips geocoding)
dotnet run --project src/GSKit.CLI -- stock <sku> --lat <lat> --long <lon>

# Wider radius, in-stock stores only
dotnet run --project src/GSKit.CLI -- stock <sku> --zip <zip> --radius 50 --in-stock-only

# JSON output
dotnet run --project src/GSKit.CLI -- stock <sku> --zip <zip> --format json

# Debug — dumps raw API response
dotnet run --project src/GSKit.CLI -- stock <sku> --zip <zip> --debug

# Product info
dotnet run --project src/GSKit.CLI -- info <sku>
```

Or build a self-contained binary and install it:

```bash
# Intel Mac
dotnet publish src/GSKit.CLI -c Release -r osx-x64 -o ./publish
sudo cp ./publish/gskit /usr/local/bin/gskit

# Apple Silicon
dotnet publish src/GSKit.CLI -c Release -r osx-arm64 -o ./publish
sudo cp ./publish/gskit /usr/local/bin/gskit

# Linux
dotnet publish src/GSKit.CLI -c Release -r linux-x64 -o ./publish
sudo cp ./publish/gskit /usr/local/bin/gskit
```

Then just:
```bash
gskit stock <sku> --zip <zip>
gskit info  <sku>
gskit --help
```

## Auth

None required. GameStop's Stores-FindStores endpoint returns full inventory data
with no cookies and no login from residential IPs. Confirmed from HAR analysis —
zero cookies sent on working requests.

If you're on a VPN or datacenter IP, Cloudflare will block you (403). Use a
residential connection.

## Config

Set defaults so you don't have to pass `--zip` every time:

```
~/.config/gskit/config.json
{
  "defaultZip":    "<your zip>",
  "defaultRadius": 100
}
```

## Confirmed endpoints

| Controller-Action | What |
|---|---|
| `Stores-FindStores` | In-store inventory by lat/lon + SKU |
| `Product-Variation` | Variant price + availability per condition |
| `SearchServices-GetSuggestions` | Autocomplete |
| `Page-loadComponent?tradeInLinks=true` | Trade-in entry |

## Phase plan

| Phase | Command | Status |
|---|---|---|
| 1 | `gskit stock` | ✅ |
| 2 | `gskit info`, `gskit sdd` | ✅ |
| 3 | `gskit search`, `gskit product` | 🔜 |
| 4 | `gskit trade` | 🔜 |
| 5 | `gskit watch` | 🔜 |
