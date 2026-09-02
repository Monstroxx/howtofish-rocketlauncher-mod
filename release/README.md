# RocketLauncherMod — "How to Fish"

Adds a rocket launcher weapon to the game. With explosions.

## Installation (easy)

### Requirements
- Steam game "How to Fish"
- **BepInEx 5.4.23+ (Windows x64)**: https://github.com/BepInEx/BepInEx/releases
  - ⚠️ **Always download `BepInEx_win_x64_5.x.x.zip`** — even on Linux!
  - The game is a Windows build and runs on Linux via Proton/Steam.
    The Unix build of BepInEx does **not** work with it.
  - Extract the zip's contents into the **game folder** (where `How to Fish.exe` lives)
  - In Steam: right-click the game → Properties → Compatibility →
    enable "Force the use of a specific Steam Play compatibility tool"
    (Proton), if it isn't already on by default
  - Start the game **once** and close it again (this lets BepInEx set itself up)

### Installing the mod

**Windows:**
1. Double-click `install.bat` → done

**Linux:**
1. Open a terminal in the extracted folder
2. Run `./install.sh` → done

> The installer finds your Steam game directory automatically. You'll
> only be asked for the path if that fails.

## Usage

1. Start the game and open a game as **host**
2. Open chat (`T` or `Enter`) and type `/rocket`
3. The rocket launcher appears in your inventory

## Configuration

After the first launch, a config file appears here:

```
<game folder>/BepInEx/config/com.kimox.rocketlauncher.cfg
```

There you can adjust speed, gravity and fuse. Changes take effect on the next launch.

## Uninstalling

- Delete `<game folder>/BepInEx/plugins/RocketLauncherMod.dll`
- Optional: delete `<game folder>/How to Fish_Data/StreamingAssets/mods/*.obj` and `*.png`

## FAQ

**Q: The mod doesn't load / the chat command doesn't do anything.**
A: Check whether `<game folder>/BepInEx/LogOutput.log` contains the entry
`RocketLauncherMod loaded`. If not: is BepInEx installed correctly?
Is there an error message in the log? → Open an issue with the log attached.

**Q: Can I use this in multiplayer with friends?**
A: Yes — but **everyone** needs the mod, not just the host. The
launcher is still only spawned by the host (`/rocket`); FishNet only
ever allows spawns on the server.

For players **without** the mod, the launcher simply doesn't appear:
the client doesn't know the mod's prefab collection and skips the
object (the log then shows `PrefabObjects collection is not found for
CollectionId 20200`). No crash, the item is just missing for them.

Settings (fire rate, ammo, model offset) are **local** — everyone
builds their launcher from their own `.cfg`. For a consistent
experience, everyone should use the same config.

**Q: I get a yellow warning when the game starts.**
A: That's the normal BepInEx console info message. Harmless.