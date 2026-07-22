#!/usr/bin/env bash
# Builds StalkerGammaGui-x86_64.AppImage:
# self-contained dotnet publish -> fetch external binaries -> AppDir -> appimagetool
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APPDIR="$ROOT/packaging/StalkerGammaGui.AppDir"
PUBLISH="$ROOT/publish"
OUT="$ROOT/StalkerGammaGui-x86_64.AppImage"

VERSION="${VERSION:-0.1.0}"

rm -rf "$PUBLISH" "$APPDIR/usr"
dotnet publish "$ROOT/src/StalkerGamma.Gui/StalkerGamma.Gui.csproj" \
    -c Release -r linux-x64 --self-contained -o "$PUBLISH" -p:Version="$VERSION"
rm -f "$PUBLISH"/*.pdb

"$ROOT/scripts/setup-deps.sh" "$PUBLISH"

mkdir -p "$APPDIR/usr/bin"
cp -r "$PUBLISH"/. "$APPDIR/usr/bin/"

APPIMAGETOOL="$ROOT/appimagetool-x86_64.AppImage"
if [[ ! -x "$APPIMAGETOOL" ]]; then
    curl -fLo "$APPIMAGETOOL" \
        "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x "$APPIMAGETOOL"
fi

# APPIMAGE_EXTRACT_AND_RUN lets appimagetool run without FUSE (CI runners, containers).
APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" "$APPDIR" "$OUT"
chmod +x "$OUT"
echo "Built: $OUT"
