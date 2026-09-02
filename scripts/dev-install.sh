#!/bin/bash
# Dev install: builds the plugin from source and drops it into a local
# "How to Fish" install for testing. For end users, use the prebuilt
# release from GitHub Releases + release/install.sh instead.
set -e

MOD_DIR="$(cd "$(dirname "$0")/.." && pwd)"

# Set HOWTOFISH_GAME_DIR to your own game folder if it's not at the
# default Steam location below.
GAME_DIR="${HOWTOFISH_GAME_DIR:-$HOME/snap/steam/common/.local/share/Steam/steamapps/common/How to Fish/How to Fish}"
DATA_DIR="$GAME_DIR/How to Fish_Data"
MANAGED="$DATA_DIR/Managed"
MODS_ASSETS="$DATA_DIR/StreamingAssets/mods"
PLUGINS="$GAME_DIR/BepInEx/plugins"

echo "== RocketLauncherMod dev installer =="
echo "Game dir: $GAME_DIR"

if [ ! -d "$MANAGED" ]; then
    echo "[!] Game not found at '$GAME_DIR'."
    echo "    Set HOWTOFISH_GAME_DIR to your install path and try again."
    exit 1
fi

# 1. Build the plugin against the game DLLs at $GAME_DIR.
dotnet build "$MOD_DIR/bepinex-plugin/RocketLauncherMod.csproj" -c Debug -v quiet \
    -p:GameDir="$GAME_DIR"
mkdir -p "$PLUGINS"
cp "$MOD_DIR/bepinex-plugin/bin/Debug/netstandard2.1/RocketLauncherMod.dll" "$PLUGINS/"
echo "[+] RocketLauncherMod.dll copied to BepInEx/plugins/"

# 2. Assets
mkdir -p "$MODS_ASSETS"
cp "$MOD_DIR/mod-assets/rocketlauncher_body.obj" "$MODS_ASSETS/"
cp "$MOD_DIR/mod-assets/rocket.obj" "$MODS_ASSETS/"
cp "$MOD_DIR/mod-assets/palette-sharks.png" "$MODS_ASSETS/"
echo "[+] Assets copied to StreamingAssets/mods/"

echo ""
echo "DONE. In-game, as host: open chat and type '/rocket'."
echo "Settings (speed, gravity, fuse) after first launch:"
echo "  $GAME_DIR/BepInEx/config/com.kimox.rocketlauncher.cfg"