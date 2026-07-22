#!/usr/bin/env bash
# Fetches the external binaries the Stalker.Gamma engine needs and places them
# in the layout it expects (relative to the app executable):
#   <target>/libcurl-impersonate.so   <target>/cacert.pem   <target>/resources/7zz
#
# Usage: scripts/setup-deps.sh <target-dir>
# Idempotent: skips anything already present.
set -euo pipefail

TARGET="${1:?usage: setup-deps.sh <target-dir>}"
mkdir -p "$TARGET/resources"

CURL_IMPERSONATE_VERSION="v2.0.0a1"
SEVENZIP_VERSION="2501"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

if [[ ! -f "$TARGET/libcurl-impersonate.so" ]]; then
    echo "==> libcurl-impersonate ${CURL_IMPERSONATE_VERSION}"
    curl -fLo "$WORK/curl.tar.gz" \
        "https://github.com/lexiforest/curl-impersonate/releases/download/${CURL_IMPERSONATE_VERSION}/libcurl-impersonate-${CURL_IMPERSONATE_VERSION}.x86_64-linux-gnu.tar.gz"
    tar -xzf "$WORK/curl.tar.gz" -C "$WORK"
    cp "$WORK/libcurl-impersonate.so.4.8.0" "$TARGET/libcurl-impersonate.so"
else
    echo "==> libcurl-impersonate.so present, skipping"
fi

if [[ ! -f "$TARGET/cacert.pem" ]]; then
    echo "==> cacert.pem"
    curl -fLo "$TARGET/cacert.pem" "https://curl.se/ca/cacert.pem"
else
    echo "==> cacert.pem present, skipping"
fi

if [[ ! -f "$TARGET/resources/7zz" ]]; then
    echo "==> 7-Zip ${SEVENZIP_VERSION}"
    curl -fLo "$WORK/7z.tar.xz" "https://www.7-zip.org/a/7z${SEVENZIP_VERSION}-linux-x64.tar.xz"
    tar -xf "$WORK/7z.tar.xz" -C "$WORK" 7zz
    cp "$WORK/7zz" "$TARGET/resources/7zz"
    chmod +x "$TARGET/resources/7zz"
else
    echo "==> resources/7zz present, skipping"
fi

echo "Done: dependencies in $TARGET"
