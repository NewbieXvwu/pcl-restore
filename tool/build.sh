#!/usr/bin/env bash
set -euo pipefail
# Builds the patcher on macOS/Linux. Requires a .NET 8 SDK.
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOTNET="${DOTNET:-}"
if [ -z "$DOTNET" ]; then
  if command -v dotnet >/dev/null 2>&1; then
    DOTNET="$(command -v dotnet)"
  else
    echo "error: no dotnet SDK found. Install .NET 8+ or set DOTNET." >&2
    exit 1
  fi
fi

cd "$DIR"
"$DOTNET" build -c Release "$DIR/PclRestore.csproj"
echo "Built: $DIR/bin/Release/net8.0/PclRestore.dll"
