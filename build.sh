#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/WebLynx2/WebLynx2.csproj"
OUT="$ROOT/publish"

# Self-contained Windows x64 — no .NET runtime install on the target PC.
# Single-file bundles native libs; first launch may extract to a cache folder.
dotnet publish "$PROJECT" \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  --output "$OUT"

echo "Published: $OUT/WebLynx2.exe"
