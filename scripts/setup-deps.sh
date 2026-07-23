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
# Pinned SHA-256 of the versioned artifacts. If a download fails verification, the upstream
# asset changed under a fixed version tag (tampering or re-release) — investigate, don't
# just bump the hash blindly.
CURL_IMPERSONATE_SHA256="64d3e9e8d8e05f820b4ec9f4491c4068df797ad8b409fe777b12005da2076234"
SEVENZIP_SHA256="4ca3b7c6f2f67866b92622818b58233dc70367be2f36b498eb0bdeaaa44b53f4"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

verify_sha256() {
    local file="$1" expected="$2"
    local actual
    actual="$(sha256sum "$file" | cut -d' ' -f1)"
    if [[ "$actual" != "$expected" ]]; then
        echo "ERROR: checksum mismatch for $file" >&2
        echo "  expected $expected" >&2
        echo "  got      $actual" >&2
        exit 1
    fi
}

if [[ ! -f "$TARGET/libcurl-impersonate.so" ]]; then
    echo "==> libcurl-impersonate ${CURL_IMPERSONATE_VERSION}"
    curl -fLo "$WORK/curl.tar.gz" \
        "https://github.com/lexiforest/curl-impersonate/releases/download/${CURL_IMPERSONATE_VERSION}/libcurl-impersonate-${CURL_IMPERSONATE_VERSION}.x86_64-linux-gnu.tar.gz"
    verify_sha256 "$WORK/curl.tar.gz" "$CURL_IMPERSONATE_SHA256"
    tar -xzf "$WORK/curl.tar.gz" -C "$WORK"
    cp "$WORK/libcurl-impersonate.so.4.8.0" "$TARGET/libcurl-impersonate.so"
else
    echo "==> libcurl-impersonate.so present, skipping"
fi

if [[ ! -f "$TARGET/cacert.pem" ]]; then
    # Mozilla's CA bundle is a rolling file (curl.se refreshes it), so it is not hash-pinned:
    # pinning would break TLS whenever the bundle is legitimately rotated. HTTPS + curl's own
    # cert chain protect this download.
    echo "==> cacert.pem"
    curl -fLo "$TARGET/cacert.pem" "https://curl.se/ca/cacert.pem"
else
    echo "==> cacert.pem present, skipping"
fi

if [[ ! -f "$TARGET/resources/7zz" ]]; then
    echo "==> 7-Zip ${SEVENZIP_VERSION}"
    curl -fLo "$WORK/7z.tar.xz" "https://www.7-zip.org/a/7z${SEVENZIP_VERSION}-linux-x64.tar.xz"
    verify_sha256 "$WORK/7z.tar.xz" "$SEVENZIP_SHA256"
    tar -xf "$WORK/7z.tar.xz" -C "$WORK" 7zz
    cp "$WORK/7zz" "$TARGET/resources/7zz"
    chmod +x "$TARGET/resources/7zz"
else
    echo "==> resources/7zz present, skipping"
fi

echo "Done: dependencies in $TARGET"
