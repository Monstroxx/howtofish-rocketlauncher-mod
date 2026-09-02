#!/bin/bash
# RocketLauncherMod - Portable installer (Linux/macOS)
# Finds the game automatically and installs the plugin + assets.
set -e

MOD_DIR="$(cd "$(dirname "$0")" && pwd)"
DLL="$MOD_DIR/RocketLauncherMod.dll"
ASSETS_DIR="$MOD_DIR/assets"

if [ ! -f "$DLL" ]; then
    echo "[!] Error: RocketLauncherMod.dll not found (must sit next to install.sh)."
    exit 1
fi

# --- Locate the game ---------------------------------------------------
GAME_DIR=""
CANDIDATES=(
    "$HOME/.steam/steam/steamapps/common/How to Fish/How to Fish"
    "$HOME/.local/share/Steam/steamapps/common/How to Fish/How to Fish"
    "$HOME/snap/steam/common/.local/share/Steam/steamapps/common/How to Fish/How to Fish"
    "$HOME/Steam/steamapps/common/How to Fish/How to Fish"
    "C:/Program Files (x86)/Steam/steamapps/common/How to Fish/How to Fish"
    "/mnt/c/Program Files (x86)/Steam/steamapps/common/How to Fish/How to Fish"
)

for c in "${CANDIDATES[@]}"; do
    if [ -d "$c" ]; then
        GAME_DIR="$c"
        break
    fi
done

# Fallback: scan Steam library folders via libraryfolders.vdf
if [ -z "$GAME_DIR" ]; then
    VDF_FILES=(
        "$HOME/.steam/steam/steamapps/libraryfolders.vdf"
        "$HOME/.local/share/Steam/steamapps/libraryfolders.vdf"
        "$HOME/snap/steam/common/.local/share/Steam/steamapps/libraryfolders.vdf"
    )
    for vdf in "${VDF_FILES[@]}"; do
        if [ -f "$vdf" ]; then
            while IFS= read -r lib; do
                c="$lib/steamapps/common/How to Fish/How to Fish"
                if [ -d "$c" ]; then
                    GAME_DIR="$c"
                    break 2
                fi
            done < <(grep -oP '"path"\s+"\K[^"]+' "$vdf" | sed 's/\\\\/\\/g')
        fi
    done
fi

if [ -z "$GAME_DIR" ]; then
    echo "[!] Game not found. Please enter the path manually."
    read -rp "Full path to the game folder (contains 'How to Fish_Data'): " GAME_DIR
    if [ ! -d "$GAME_DIR/How to Fish_Data" ]; then
        echo "[!] Invalid path. Aborting."
        exit 1
    fi
fi

echo "[+] Game found: $GAME_DIR"
DATA_DIR="$GAME_DIR/How to Fish_Data"
PLUGINS="$GAME_DIR/BepInEx/plugins"
MODS_ASSETS="$DATA_DIR/StreamingAssets/mods"

# --- Check BepInEx ------------------------------------------------------
if [ ! -d "$GAME_DIR/BepInEx" ]; then
    echo "[!] BepInEx is NOT installed."
    echo "    IMPORTANT: download the Windows build (BepInEx_win_x64), even on Linux!"
    echo "    The game runs via Proton -> the Unix BepInEx build won't work."
    echo "    1. Download: https://github.com/BepInEx/BepInEx/releases"
    echo "       -> BepInEx_win_x64_5.x.x.zip"
    echo "    2. Extract it into the game folder: $GAME_DIR"
    echo "    3. Start the game ONCE (this creates the BepInEx folder), then run the installer again."
    read -rp "Or enter the path to an already-extracted BepInEx folder (Enter = abort): " bex
    if [ -n "$bex" ] && [ -f "$bex/core/BepInEx.dll" ]; then
        cp -r "$bex"/* "$GAME_DIR/"
        echo "[+] BepInEx copied to $GAME_DIR"
    else
        echo "Aborting."
        exit 1
    fi
fi

# --- Install plugin + assets ---------------------------------------------
mkdir -p "$PLUGINS" "$MODS_ASSETS"
cp "$DLL" "$PLUGINS/"
echo "[+] RocketLauncherMod.dll installed"

if [ -d "$ASSETS_DIR" ]; then
    cp "$ASSETS_DIR"/*.obj "$ASSETS_DIR"/*.png "$MODS_ASSETS/" 2>/dev/null || true
    echo "[+] Assets installed"
else
    echo "[!] Warning: assets/ folder missing - the mod may not load any models."
fi

echo ""
echo "=== DONE ==="
echo "Start the game, open a game as host, open chat, type '/rocket'."
echo "Config after first launch: $GAME_DIR/BepInEx/config/com.kimox.rocketlauncher.cfg"