#!/bin/bash
# Builds the plugin, packages a release zip (DLL + assets + end-user
# installers) and publishes it as the newest GitHub Release.
#
# Usage: ./release.sh v1.1.0
#
# Requires: dotnet, zip, GitHub CLI (gh, authenticated), and the game
# installed locally (set HOWTOFISH_GAME_DIR if it's not at the default
# Steam path).
set -e

TAG="$1"
if [ -z "$TAG" ]; then
    echo "Usage: $0 <tag>   (e.g. ./release.sh v1.1.0)"
    exit 1
fi

MOD_DIR="$(cd "$(dirname "$0")" && pwd)"
GAME_DIR="${HOWTOFISH_GAME_DIR:-$HOME/snap/steam/common/.local/share/Steam/steamapps/common/How to Fish/How to Fish}"
BUILD_DIR="$MOD_DIR/bepinex-plugin/bin/Release/netstandard2.1"
STAGE_DIR="$MOD_DIR/.release-stage"
ZIP_NAME="RocketLauncherMod-$TAG.zip"

if [ ! -d "$GAME_DIR/How to Fish_Data/Managed" ]; then
    echo "[!] Game not found at '$GAME_DIR'."
    echo "    Set HOWTOFISH_GAME_DIR to your install path and try again."
    exit 1
fi

echo "[1/4] Building plugin (Release)..."
dotnet build "$MOD_DIR/bepinex-plugin/RocketLauncherMod.csproj" -c Release -v quiet \
    -p:GameDir="$GAME_DIR"

echo "[2/4] Staging release contents..."
rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR/assets"
cp "$BUILD_DIR/RocketLauncherMod.dll" "$STAGE_DIR/"
cp "$MOD_DIR/mod-assets/"*.obj "$MOD_DIR/mod-assets/"*.png "$STAGE_DIR/assets/"
cp "$MOD_DIR/release/install.sh" "$MOD_DIR/release/install.bat" "$MOD_DIR/release/README.md" "$STAGE_DIR/"
chmod +x "$STAGE_DIR/install.sh"

echo "[3/4] Zipping $ZIP_NAME..."
(cd "$STAGE_DIR" && zip -qr "$MOD_DIR/$ZIP_NAME" .)

echo "[4/4] Publishing GitHub release $TAG (latest)..."
gh release create "$TAG" "$MOD_DIR/$ZIP_NAME" \
    --title "$TAG" \
    --generate-notes \
    --latest

rm -rf "$STAGE_DIR"
echo ""
echo "DONE. $ZIP_NAME published as $TAG."