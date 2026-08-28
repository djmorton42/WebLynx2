#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/WebLynx2.Tests/WebLynx2.Tests.csproj"

# Local dev on macOS/Linux: run the test suite with the installed SDK.
dotnet test "$PROJECT" "$@"
