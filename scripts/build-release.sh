#!/usr/bin/env bash
set -euo pipefail

# Run from anywhere: move to the repo root (parent of scripts/)
cd "$(dirname "$0")/.."

NAME="gskit"
VERSION="1.0.1"   # keep in sync with <Version> in GSKit.CLI.csproj
PROJ="src/GSKit.CLI/GSKit.CLI.csproj"
DIST="dist"
TARGETS=(win-x64 win-arm64 osx-x64 osx-arm64 linux-x64 linux-arm64)

rm -rf "$DIST" build
mkdir -p "$DIST"

for rid in "${TARGETS[@]}"; do
  out="build/$rid"
  dotnet publish "$PROJ" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:Version="$VERSION" -o "$out"

  base="$NAME-$VERSION-$rid"
  if [[ "$rid" == win-* ]]; then
    cp "$out/gskit.exe" "$DIST/$base.exe"
  else
    tar -czf "$DIST/$base.tar.gz" -C "$out" gskit
  fi
done

(cd "$DIST" && shasum -a 256 * > SHA256SUMS.txt)
ls -lh "$DIST"
