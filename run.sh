#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/WebLynx2/WebLynx2.csproj"
VIEWS_DIR="$(cd "$ROOT/../WebLynx2-Sso-View-Package-Modern" && pwd)"

if [[ ! -d "$VIEWS_DIR" ]]; then
  echo "Views package not found: $VIEWS_DIR" >&2
  exit 1
fi

APPSETTINGS="$ROOT/WebLynx2/appsettings.json"
python3 - "$APPSETTINGS" "$VIEWS_DIR" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
views_dir = sys.argv[2]
data = json.loads(path.read_text())
data.setdefault("Server", {})["ViewsDirectory"] = views_dir
path.write_text(json.dumps(data, indent=2) + "\n")
PY

# Local dev on macOS/Linux: run with the installed SDK.
dotnet run --project "$PROJECT" "$@"
