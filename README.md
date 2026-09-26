# GSKit

GameStop reverse engineering toolkit. Inventory, trade-ins, pricing, store data, recon.

## Setup (Mac, 2 commands)

```bash
brew install dotnet
git clone https://github.com/you/gskit && cd gskit
dotnet build
```

## Phase 1 — Stock checker (works now)

```bash
# By zip code
dotnet run --project src/GSKit.CLI -- stock 133857 --zip <zip> --radius 150

# By coordinates (faster, skips geocoding)
dotnet run --project src/GSKit.CLI -- stock 133857 --lat <lat>337 --long <lon>479 --radius 150

# In-stock stores only, JSON output
dotnet run --project src/GSKit.CLI -- stock 133857 --zip <zip> --in-stock-only --format json

# Manual session (if Chrome cookie read fails)
# Get these from DevTools → Application → Cookies on gamestop.com
dotnet run --project src/GSKit.CLI -- stock 133857 --zip <zip> \
  --session "dwsid=abc123;cf_clearance=xyz789"
```

## Auth

GSKit reads cookies directly from your Chrome profile — no browser automation needed.

```
Mac Chrome:  ~/Library/Application Support/Google/Chrome/Default/Cookies
Mac Edge:    ~/Library/Application Support/Microsoft Edge/Default/Cookies
```

If Chrome is running when you call gskit, it copies the SQLite file to a temp path first
(Chrome holds a write lock on the original). Decrypts v10/v20 AES-GCM values via Keychain.

If Chrome cookie read fails (e.g. no Chrome installed), gskit tries a cold seed — hits the
product page directly and captures the dwsid cookie. Works when Cloudflare isn't in hard-block
mode, which it usually isn't for normal residential IPs.

## Cloudflare

GameStop uses Cloudflare. The `cf_clearance` cookie is valid for ~30min–2hr.
When it expires you'll get a `CloudflareBlockException` — just let gskit re-read your Chrome
cookies (which are always fresh from your normal browsing sessions).

For fully headless/CI use: Phase 3 work — custom TLS JA3 fingerprint matching Chrome's.

## Confirmed endpoints (from browser recon)

| Controller-Action | What |
|---|---|
| `Stores-FindStores` | **In-store inventory by lat/lon + SKU** — Phase 1 |
| `Stores-InventorySearch` | Modal HTML shell loader |
| `Product-Variation` | Variant price + availability per condition/platform |
| `SearchServices-GetSuggestions` | Autocomplete (no auth) |
| `Page-loadComponent?tradeInLinks=true` | Trade-in entry point |
| `Cart-AddProduct` | Add to cart |
| `Product-IncludeLastVisited` | Related SKUs / recommendations |

## Phase plan

| Phase | Command | Status |
|---|---|---|
| 1 | `gskit stock` | ✅ Built |
| 2 | `gskit search`, `gskit product` | 🔜 |
| 3 | `gskit trade` | 🔜 |
| 4 | `gskit watch` | 🔜 |
| 5 | `gskit recon` | 🔜 |
| 6 | `gskit auth` | 🔜 |
