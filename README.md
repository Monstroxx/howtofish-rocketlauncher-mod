# RocketLauncherMod

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for the Steam game
**"How to Fish"** that adds a rocket launcher weapon — direct-fire and
homing missiles, explosions with knockback, and chat-command
configuration.

This repo contains **only the mod's own code and assets**. It does not
contain any decompiled game source: "How to Fish" is closed-source and
its decompiled code is not ours to redistribute. The plugin is a
regular Harmony/BepInEx mod that patches the game's unmodified DLL at
runtime — it never ships or requires the game's own code.

## Get the mod

Download the latest release from the
[Releases page](../../releases/latest) and follow
[release/README.md](release/README.md) for installation.

## Chat commands

| Command | Effect |
|---|---|
| `/rocket` | Spawns the rocket launcher in the host's inventory |
| `/rocketbuy` | Buys the homing-missile upgrade (cost goes to the server till) |
| `/rocketcfg` | Adjusts fire rate, ammo, reload speed, recoil, knockback |
| `/rocketmodel` | Model/offset tools for lining up the weapon in-hand |

## Repository layout

- `bepinex-plugin/` — plugin source (`RocketLauncherMod.csproj`)
- `mod-assets/` — 3D models and texture the plugin loads at runtime
- `release/` — end-user installers (`install.sh`, `install.bat`) and
  the install guide, packaged into every release zip
- `scripts/dev-install.sh` — build-and-install helper for local testing
- `release.sh` — builds, packages and publishes a new GitHub Release

## Building from source

You need your own legally-owned copy of "How to Fish" installed
locally — the build references that install's DLLs
(`Assembly-CSharp.dll`, `UnityEngine*.dll`, `FishNet.Runtime.dll`,
BepInEx's `0Harmony.dll`/`BepInEx.dll`) and none of them are shipped in
this repo.

```bash
# If your game isn't at the default Steam path baked into the .csproj:
export HOWTOFISH_GAME_DIR="/path/to/How to Fish/How to Fish"

dotnet build bepinex-plugin/RocketLauncherMod.csproj -c Debug -p:GameDir="$HOWTOFISH_GAME_DIR"
```

To build and install straight into your local game copy for testing:

```bash
HOWTOFISH_GAME_DIR="/path/to/How to Fish/How to Fish" ./scripts/dev-install.sh
```

## Cutting a release

GitHub-hosted Actions runners can't build this plugin — the build
needs the game's proprietary DLLs, which can't legally be committed to
the repo or fetched in CI. Releases are built and published locally
instead, from a machine that has the game installed:

```bash
./release.sh v1.2.0
```

This builds the plugin in Release mode, packages
`RocketLauncherMod.dll` + `mod-assets/` + the `release/` installers
into a zip, and publishes it as the newest GitHub Release via the
[GitHub CLI](https://cli.github.com/) (`gh`, must be authenticated).