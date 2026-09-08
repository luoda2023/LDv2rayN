#!/usr/bin/env bash
# Download Xray-core for Windows and place into bin/xray
# Modeled on the download_xray() pattern from package-debian.sh.
#
# Usage:
#   bash tools/download-xray-core.sh
#   bash tools/download-xray-core.sh --config Release
#   bash tools/download-xray-core.sh --config Debug --arch x86
#
# Default: Release win-x64 -> v2rayN/bin/Release/net10.0-windows10.0.19041.0/win-x64/bin/xray/
set -euo pipefail

CONFIG="Release"
ARCH="x64"
while [ $# -gt 0 ]; do
    case "$1" in
        --config) CONFIG="$2"; shift 2 ;;
        --arch)   ARCH="$2"; shift 2 ;;
        *) echo "Unknown arg: $1"; exit 1 ;;
    esac
done

cd "$(dirname "$0")/.."

if [ "$CONFIG" = "Release" ]; then
    TARGET="v2rayN/bin/Release/net10.0-windows10.0.19041.0/win-x64/bin/xray"
else
    TARGET="v2rayN/bin/Debug/net10.0-windows10.0.19041.0/bin/xray"
fi
echo "[*] Target: $TARGET"
mkdir -p "$TARGET"

# 1) Try the official XTLS/Xray-core releases
VER="$(curl -fsSL https://api.github.com/repos/XTLS/Xray-core/releases/latest \
    | grep -Eo '"tag_name":\s*"v[^"]+"' \
    | sed -E 's/.*"v([^"]+)".*/\1/' \
    | head -n1 || true)"

if [ -z "${VER:-}" ]; then
    echo "[!] Could not fetch latest Xray version from GitHub API"
else
    echo "[+] Latest Xray-core version: v${VER}"
fi

URL=""
if [ "$ARCH" = "x64" ]; then
    URL="https://github.com/XTLS/Xray-core/releases/download/v${VER}/Xray-win64.zip"
else
    URL="https://github.com/XTLS/Xray-core/releases/download/v${VER}/Xray-win32.zip"
fi

# 2) Fallback: 2dust mirror (no API resolution needed)
if [ -z "${VER:-}" ]; then
    echo "[*] Falling back to 2dust mirror..."
    if [ "$ARCH" = "x64" ]; then
        URL="https://raw.githubusercontent.com/2dust/v2rayN-core-bin/refs/heads/master/xray-windows-64.zip"
    else
        URL="https://raw.githubusercontent.com/2dust/v2rayN-core-bin/refs/heads/master/xray-windows-32.zip"
    fi
fi

echo "[+] Download: $URL"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

curl -fL "$URL" -o "$TMP/xray.zip"

# Prefer unzip; fall back to PowerShell on Windows
if command -v unzip >/dev/null 2>&1; then
    unzip -q "$TMP/xray.zip" -d "$TMP"
else
    echo "[*] unzip not found, using PowerShell Expand-Archive"
    powershell -NoProfile -Command "Expand-Archive -Path '$TMP/xray.zip' -DestinationPath '$TMP' -Force"
fi

# Copy xray.exe and any companion files into target
echo "[+] Copying files to $TARGET"
cp -v "$TMP"/*.exe "$TARGET"/ 2>/dev/null || true
cp -v "$TMP"/*.dll "$TARGET"/ 2>/dev/null || true
cp -v "$TMP"/*.json "$TARGET"/ 2>/dev/null || true
cp -v "$TMP"/*.md   "$TARGET"/ 2>/dev/null || true
# If unzip created a nested directory, recurse one level
for d in "$TMP"/*/; do
    [ -d "$d" ] || continue
    cp -v "$d"*.exe "$TARGET"/ 2>/dev/null || true
    cp -v "$d"*.dll "$TARGET"/ 2>/dev/null || true
    cp -v "$d"*.json "$TARGET"/ 2>/dev/null || true
done

echo ""
echo "=== $TARGET contents ==="
ls -la "$TARGET"

if [ -f "$TARGET/xray.exe" ]; then
    echo ""
    echo "[OK] xray.exe present. Restart LDv2rayN.exe to pick it up."
else
    echo ""
    echo "[!] xray.exe not found. The zip may have a different filename or layout."
    echo "    Inspect $TARGET and rename if needed."
fi
