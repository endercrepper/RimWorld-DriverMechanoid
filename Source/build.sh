#!/usr/bin/env bash
# Builds DMS_DriverMechanoid.dll and copies it into ../Assemblies/.
# Requires the .NET SDK (https://dot.net). On first run it restores NuGet packages
# (Krafs.RimWorld.Ref + Lib.Harmony), which provide the RimWorld/Harmony compile-time
# references. No RimWorld installation is needed to build.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$HERE"

CONFIG="${1:-Release}"

echo ">> Restoring + building (Configuration=$CONFIG)..."
dotnet build -c "$CONFIG"

DEST_DIR="$HERE/../Assemblies"
DEST_DLL="$DEST_DIR/DMS_DriverMechanoid.dll"

# Locate the built DLL across whatever TFM the project targets (net48, net6.0, ...).
SRC_DLL="$(find "$HERE/bin/$CONFIG" -name 'DMS_DriverMechanoid.dll' -type f | head -1)"
if [[ -z "$SRC_DLL" || ! -f "$SRC_DLL" ]]; then
  echo "!! Build did not produce DMS_DriverMechanoid.dll under $HERE/bin/$CONFIG" >&2
  exit 1
fi

mkdir -p "$DEST_DIR"
cp -f "$SRC_DLL" "$DEST_DLL"
echo ">> Installed: $DEST_DLL"
