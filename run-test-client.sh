#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/TcpTestClient/TcpTestClient.csproj"

# Usage: ./run-test-client.sh <ip> <port>
# Example: ./run-test-client.sh 127.0.0.1 8080
dotnet run --project "$PROJECT" -- "$@"
