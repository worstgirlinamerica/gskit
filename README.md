# gskit

GameStop reverse engineering toolkit: inventory, trade-ins, pricing, and store data.

## Install

Download the binary for your system from the [Releases page](https://github.com/worstgirlinamerica/gskit/releases).

| Platform | File |
|---|---|
| Windows x64 | `win-x64.exe` |
| Windows ARM64 | `win-arm64.exe` |
| macOS Intel | `osx-x64.tar.gz` |
| macOS Apple Silicon | `osx-arm64.tar.gz` |
| Linux x64 | `linux-x64.tar.gz` |
| Linux ARM64 | `linux-arm64.tar.gz` |

**macOS:** Extract and allow the binary to run, Gatekeeper will likely block them by default because builds are not signed, so run the commands below

```bash
tar -xzf gskit-<version>-osx-arm64.tar.gz
xattr -d com.apple.quarantine gskit
sudo mv gskit /usr/local/bin/
```

**Windows:** Run the `.exe` from a terminal, or move it to a folder on your `PATH`.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/worstgirlinamerica/gskit
cd gskit
dotnet build
```

To build a release binary for your platform:

```bash
dotnet publish src/GSKit.CLI -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

Or run `scripts/build-release.sh` to build all six targets.

## Usage

```bash
gskit stock <sku> --zip <zip>
gskit stock <sku> --lat <lat> --long <lon>        # skips geocoding
gskit stock <sku> --zip <zip> --radius 50 --in-stock-only
gskit stock <sku> --zip <zip> --format json
gskit stock <sku> --zip <zip> --debug             # dumps the raw API response
gskit info <sku>
gskit --help
```

<!-- Add sections for: trade, tiles, store-availability, probe, sdd -->

## Config

Set defaults so you don't have to pass `--zip` each time. Create `~/.config/gskit/config.json`:

```json
{
  "defaultZip": "<your zip>",
  "defaultRadius": 100
}
```

## Auth

<!-- Confirm this still matches the current code -->
Stock and info lookups need no login. Requests from VPN or datacenter IPs may be blocked by Cloudflare (403). Use a residential connection.
