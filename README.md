# gskit

GameStop reverse engineering toolkit. Inventory, trade-ins, pricing, store data.

## Install

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/worstgirlinamerica/gskit
cd gskit
dotnet build
```

## Usage

```bash
# Check in-store inventory by zip code
dotnet run --project src/GSKit.CLI -- stock 133857 --zip <zip>

# By coordinates (faster, skips geocoding)
dotnet run --project src/GSKit.CLI -- stock 133857 --lat <lat> --long <lon>

# Wider radius, in-stock stores only
dotnet run --project src/GSKit.CLI -- stock 133857 --zip 10001 --radius 50 --in-stock-only

# JSON output
dotnet run --project src/GSKit.CLI -- stock 133857 --zip <zip> --format json

# Debug — dumps raw API response
dotnet run --project src/GSKit.CLI -- stock 133857 --zip <zip> --debug
```

Or build a self-contained binary:

```bash
# Apple Silicon
dotnet publish src/GSKit.CLI -c Release -r osx-arm64 --self-contained -o out/

# Intel Mac
dotnet publish src/GSKit.CLI -c Release -r osx-x64 --self-contained -o out/

# Linux
dotnet publish src/GSKit.CLI -c Release -r linux-x64 --self-contained -o out/
```

## Auth

None required. GameStop's Stores-FindStores endpoint returns full inventory data
with no cookies and no login from residential IPs. Confirmed from HAR analysis —
zero cookies sent on working requests.

If you're on a VPN or datacenter IP, Cloudflare will block you (403). Use a
residential connection.

## Confirmed endpoints

| Controller-Action | What |
|---|---|
| `Stores-FindStores` | In-store inventory by lat/lon + SKU — **Phase 1** |
| `Product-Variation` | Variant price + availability per condition |
| `SearchServices-GetSuggestions` | Autocomplete |
| `Page-loadComponent?tradeInLinks=true` | Trade-in entry |

## Phase plan

| Phase | Command | Status |
|---|---|---|
| 1 | `gskit stock` | ✅ |
| 2 | `gskit search`, `gskit product` | 🔜 |
| 3 | `gskit trade` | 🔜 |
| 4 | `gskit watch` | 🔜 |
