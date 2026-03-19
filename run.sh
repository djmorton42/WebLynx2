#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/WebLynx2/WebLynx2.csproj"

# Local dev on macOS/Linux: run with the installed SDK.
dotnet run --project "$PROJECT" "$@"
