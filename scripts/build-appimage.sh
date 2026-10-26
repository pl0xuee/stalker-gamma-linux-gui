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

# Pinned to a versioned release (not the rolling "continuous" one, which is re-published in place
# and broke this pin). If the check fails, the asset changed under a fixed tag: investigate, don't
# just bump the hash (a failed build is the point — it stops a silently-swapped tool from shipping).
APPIMAGETOOL_VERSION="1.9.1"
APPIMAGETOOL="$ROOT/appimagetool-x86_64.AppImage"
APPIMAGETOOL_SHA256="ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"
if [[ ! -x "$APPIMAGETOOL" ]]; then
    curl -fLo "$APPIMAGETOOL" \
        "https://github.com/AppImage/appimagetool/releases/download/${APPIMAGETOOL_VERSION}/appimagetool-x86_64.AppImage"
    actual="$(sha256sum "$APPIMAGETOOL" | cut -d' ' -f1)"
    if [[ "$actual" != "$APPIMAGETOOL_SHA256" ]]; then
        echo "ERROR: appimagetool checksum mismatch (expected $APPIMAGETOOL_SHA256, got $actual)" >&2
        rm -f "$APPIMAGETOOL"
        exit 1
    fi
    chmod +x "$APPIMAGETOOL"
fi

# APPIMAGE_EXTRACT_AND_RUN lets appimagetool run without FUSE (CI runners, containers).
APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" "$APPDIR" "$OUT"
chmod +x "$OUT"
echo "Built: $OUT"
