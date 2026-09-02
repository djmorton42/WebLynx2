#!/usr/bin/env bash
# WebLynx2 Distribution Build Script
# Creates complete distributable packages for Windows and macOS.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/WebLynx2/WebLynx2.csproj"
VIEWS_SRC="$ROOT/WebLynx2/Views"

cd "$ROOT"

echo "Building WebLynx2 Distributions..."

# Clean previous builds
echo "Cleaning previous builds..."
rm -rf publish/ WebLynx2-*-dist/ WebLynx2-*-x64.zip

# Build for Windows
echo "Building for Windows (x64)..."
dotnet publish "$PROJECT" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/win-x64/

# Build for macOS
echo "Building for macOS (x64)..."
dotnet publish "$PROJECT" \
  -c Release \
  -r osx-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/osx-x64/

create_distribution() {
    local platform=$1
    local executable_name=$2
    local dist_name=$3

    echo "Creating $platform distribution structure..."
    DIST_DIR="WebLynx2-$platform-dist"
    rm -rf "$DIST_DIR"
    mkdir -p "$DIST_DIR"

    echo "Copying main application files..."
    cp "publish/$platform/$executable_name" "$DIST_DIR/"

    # Dist appsettings must use relative Views path (not local absolute paths)
    cat > "$DIST_DIR/appsettings.json" << 'EOF'
{
  "Event": {
    "Title": "",
    "Subtitle": "",
    "UnofficialResultsPath": ".",
    "OfficialResultsPath": ".",
    "FileEncoding": "ISO-8859-1",
    "PollingIntervalSeconds": 1,
    "DelayedDisplaySeconds": 3
  },
  "Server": {
    "ResultsPort": 8081,
    "ClockPort": 8080,
    "HttpPort": 5001,
    "ViewsDirectory": "Views"
  }
}
EOF

    if [ -f "VERSION.txt" ]; then
        echo "Copying VERSION.txt..."
        cp VERSION.txt "$DIST_DIR/"
    fi

    if [ -f "LICENSE" ]; then
        echo "Copying LICENSE..."
        cp LICENSE "$DIST_DIR/"
    fi

    if [ -f "README.md" ]; then
        echo "Copying README.md..."
        cp README.md "$DIST_DIR/"
    fi

    if [ -d "docs" ]; then
        echo "Copying docs directory..."
        cp -r docs "$DIST_DIR/"
    fi

    echo "Copying Views directory..."
    cp -r "$VIEWS_SRC" "$DIST_DIR/"

    if [ -f "etc/WebLynx.lss" ]; then
        echo "Copying WebLynx.lss configuration file..."
        cp etc/WebLynx.lss "$DIST_DIR/"
    fi

    echo "Creating configuration guide..."
    cat > "$DIST_DIR/CONFIGURATION.md" << 'EOF'
# WebLynx2 Configuration Guide

## Quick Start

`appsettings.json` configures the application.

### Event Settings
- `Title`: Meet title shown on views
- `Subtitle`: Event subtitle shown on views
- `UnofficialResultsPath`: Path to unofficial results files
- `OfficialResultsPath`: Path to official results files
- `FileEncoding`: Encoding for results files (default: ISO-8859-1)
- `PollingIntervalSeconds`: How often to poll results files (default: 1)
- `DelayedDisplaySeconds`: Delay before showing lap times (default: 3)

### Server Settings
- `ResultsPort`: Port for results data from FinishLynx (default: 8081)
- `ClockPort`: Port for clock/timing data from FinishLynx (default: 8080)
- `HttpPort`: Web interface port (default: 5001)
- `ViewsDirectory`: Path to views folder (default: `Views`)

## Views

Stock distribution includes:
- `Views/example` — demo view
- `Views/shared` — shared helpers used by views
- `Views/view.yaml` — default view configuration

Replace or extend the `Views` directory with a view package as needed.

## Troubleshooting

- Ensure ports 8080, 8081, and 5001 are not blocked by firewall
- Verify FinishLynx is configured to send data to the correct ports
EOF

    echo "Creating $platform distribution package..."
    (
      cd "$DIST_DIR"
      zip -r "../$dist_name" . -x "*.DS_Store" "Thumbs.db"
    )

    rm -rf "$DIST_DIR"
}

create_distribution "win-x64" "WebLynx2.exe" "WebLynx2-win-x64.zip"
create_distribution "osx-x64" "WebLynx2" "WebLynx2-macos-x64.zip"

echo ""
echo "Distribution build complete!"
echo "Distribution packages created:"
echo "  - WebLynx2-win-x64.zip (Windows)"
echo "  - WebLynx2-macos-x64.zip (macOS)"
echo ""
echo "Each package includes:"
echo "  - Main application executable"
echo "  - appsettings.json (ViewsDirectory=Views)"
echo "  - WebLynx.lss (FinishLynx configuration template)"
echo "  - Views/ (example view + shared helpers)"
echo "  - CONFIGURATION.md"
