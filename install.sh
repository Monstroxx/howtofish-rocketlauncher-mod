#!/bin/bash
set -e

GAME_DIR="/home/kimox/snap/steam/common/.local/share/Steam/steamapps/common/How to Fish/How to Fish"
DATA_DIR="$GAME_DIR/How to Fish_Data"
MANAGED="$DATA_DIR/Managed"
MOD_DIR="$(cd "$(dirname "$0")" && pwd)"
MODS_ASSETS="$DATA_DIR/StreamingAssets/mods"
PLUGINS="$GAME_DIR/BepInEx/plugins"

echo "== RocketLauncherMod Installer (BepInEx) =="

# 1. Altes DLL-Swap-Backup nur noch melden, NICHT mehr zurueckspielen.
#    Frueher wurde .original hier ueber Assembly-CSharp.dll kopiert. Nach einem
#    Spiel-Update ist das Backup aelter als die installierte DLL - das Zurueckspielen
#    hat das Update stillschweigend rueckgaengig gemacht. Das Plugin braucht das nicht:
#    es laeuft per BepInEx gegen die unveraenderte Spiel-DLL.
if [ -f "$MANAGED/Assembly-CSharp.dll.original" ]; then
    if ! cmp -s "$MANAGED/Assembly-CSharp.dll" "$MANAGED/Assembly-CSharp.dll.original"; then
        echo "[!] Assembly-CSharp.dll.original weicht von der installierten DLL ab."
        echo "    Das ist ein Ueberbleibsel des alten DLL-Swap-Wegs (vermutlich vor einem"
        echo "    Spiel-Update angelegt) und wird NICHT zurueckgespielt. Kann geloescht werden:"
        echo "    rm '$MANAGED/Assembly-CSharp.dll.original'"
    fi
fi

# 2. Plugin bauen und installieren
dotnet build "$MOD_DIR/bepinex-plugin/RocketLauncherMod.csproj" -c Debug -v quiet
mkdir -p "$PLUGINS"
cp "$MOD_DIR/bepinex-plugin/bin/Debug/netstandard2.1/RocketLauncherMod.dll" "$PLUGINS/"
echo "[+] RocketLauncherMod.dll nach BepInEx/plugins/ kopiert"

# 3. Assets
mkdir -p "$MODS_ASSETS"
cp "$MOD_DIR/mod-assets/rocketlauncher_body.obj" "$MODS_ASSETS/"
cp "$MOD_DIR/mod-assets/rocket.obj" "$MODS_ASSETS/"
cp "$MOD_DIR/mod-assets/palette-sharks.png" "$MODS_ASSETS/"
echo "[+] Assets nach StreamingAssets/mods/ kopiert"

echo ""
echo "FERTIG. Im Spiel (als Host): Chat oeffnen und '/rocket' eingeben."
echo "Einstellungen (Speed, Gravity, Fuse) nach dem ersten Start in:"
echo "  $GAME_DIR/BepInEx/config/com.kimox.rocketlauncher.cfg"
