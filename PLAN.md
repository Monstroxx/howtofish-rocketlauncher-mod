# How to Fish — Custom Weapon Mod: "Asimov Rocket Launcher"

## Analysis Results

### Game
- **Engine:** Unity **6000.4.4f1** (Unity 6), URP, FishNet (multiplayer), Steam
- **Item registration:** `GameInfo.Awake()` loads all items via `Resources.LoadAll<Item>("Items")` from `resources.assets` → name+ID dictionary
- **Weapon pipeline:** `Weapon` (Tool→Item) → `WeaponInfo` (byte `ProjectileType` = index in `ProjectileManager._types[]`) → projectile rendering via GPU instancing through `InstanceManager` (name→mesh+material, NO GameObject)
- **Explosions:** `ExplosionManager.ServerExplode(Item, ExplosionInfo)` — complete system already present (damage radius, knockback, killing fish, boat force, screenshake, VFX/sound)
- **Cheats:** `/spawn <name>` in chat — but `CheatsEnabled` is tied to `SteamManager.IsDev` (hardcoded Steam IDs) → must be enabled by the mod
- **Build:** the decompiled `Assembly-CSharp.csproj` **now builds with 0 errors** against the game DLLs (HintPaths fixed, `UnityEngine.UIElementsModule` added). So swapping the DLL is feasible.

### FBX (Asimov Weapon Pack)
- **Contains NO real animations** — all 37 takes are 1-frame rest poses (a pose library). Animations have to be built from scratch (but the rig makes it easy)
- **Rig structure:** only 3 relevant bones: `Body` (len 0.71, root), `Trigger` (child of Body), `Rocket` (child of Body, at the back of the tube)
- **Mesh states:** `Rocket` (loaded rocket visible) + `Rocket Fired` (empty tube) — ideal for fire/reload anims
- **Extracted:** `/tmp/opencode/fbx_out/AsimovRocketLauncher_clean.fbx` (4 MB, rocket launcher parts only)
- **Materials:** `GunMetalNewPallete`, `PlasticNewPallete`
- ⚠️ **Missing texture:** `Palette -Sharks.png` is NOT embedded in the FBX (only a path reference to `/run/media/Hans/...`). Without the PNG it'll be untextured/white. → Please get the palette PNG from the pack, otherwise we'll replace it with flat colors.

---

## Recommended Approach: Modified Assembly-CSharp.dll + AssetBundle

(A full rip with AssetRipper would be "cleaner", but rarely runs 1:1. The hybrid approach is more robust and faster.)

### Step 1 — Unity Editor Project (Unity 6000.4.4f1)
1. Create an empty URP project
2. Import `AsimovRocketLauncher_clean.fbx`
3. **Build the animations ourselves** (the pack has none — only 1-frame rest poses). Legacy clips with the exact names `Weapon.cs` expects: `Idle`, `Fire`, `FireLast`, `Reload`, `ReloadLast`, `Inspect`. The rig is minimal (bones: `Body`, `Trigger`, `Rocket`): `Idle` = rest pose loop, `Fire` = trigger pulled back + rocket bone scale 0 + recoil (~10f), `Reload` = rocket goes back in (~40f). Export as a **Legacy animation** (not Humanoid), Unity import set to "Legacy"
4. Import `Palette -Sharks.png` (if available), switch materials to URP
5. **Build an AssetBundle** (StandaloneLinux64) with:
   - Launcher mesh + material
   - Rocket mesh + material (for InstanceManager)
   - Animation clips
   - Optional: our own explosion VFX (otherwise we use the game's own via `ParticleManager` names)

### Step 2 — Code Changes in the Decompiled Project
All changes go directly into Assembly-CSharp (we own the code):

1. **`RocketLauncherMod.cs` (new)** — bootstrap via `[RuntimeInitializeOnLoadMethod]`:
   - Load the AssetBundle from `How to Fish_Data/StreamingAssets/mods/`
   - After `GameInfo.Awake`: append a new **ProjectileType** to `ProjectileManager._types` (index = length, e.g. 3) — direct field access is possible since it's the same assembly
   - Register a new **InstanceType** `"RocketMesh"` in `InstanceManager` (mesh+material from the bundle, also into the `_instanceTypes` array + dict, otherwise `RenderBatches()` won't render it)
   - Weapon prefab: clone an existing weapon item from `GameInfo` (template), swap mesh/renderer, set WeaponInfo (ProjectileType=new index, gravity ~5 for an arcing shot, force), register it as `"RocketLauncher"` + a free item ID (e.g. 200) in `GameInfo` → `/spawn rocketlauncher` then works immediately
   - `ClientSettings.ToggleCheats(true)` in the bootstrap (bypasses `SteamManager.IsDev`)
2. **`ProjectileManager.Hit()`** — extension:
   ```csharp
   if (projectile.TypeId == RocketLauncherMod.RocketTypeId) {
       ExplosionManager.ServerExplodeAt(hit.point, projectile.Owner, RocketLauncherMod.ExplosionInfo);
       // no LandProjectile, no LandedMesh
   }
   ```
3. **`ExplosionManager.cs`** — new method `ServerExplodeAt(Vector3 pos, Player player, ExplosionInfo info)`: refactor of `ServerExplode` using `pos` instead of `item.transform.position` (the rocket has no item GameObject). Observer RPC for VFX/sound/screenshake analogous to `ObserverExplode`.
4. **`Weapon.cs`** — not required, but optionally: `_projectileCountPerShot=1`, high `_recoilKnockback`, `_noShootingDuringShootAnim=true` via prefab config in the bootstrap.

### Step 3 — ExplosionInfo (configurable in the bootstrap)
```csharp
Damage: 150 | DamageRadius: 6 | ForceRadius: 4
ItemForce: 1500 | BoatForce: 1500 | PlayerForce: 60
ExplosionParticleName: <existing ParticleManager effect>  // e.g. dynamite explosion
ExplosionSoundName: <existing sound>                      // e.g. "Explosion_V"
```

### Step 4 — Deployment
1. `dotnet build` → new `Assembly-CSharp.dll` → copy into `Managed/` (back up the original!)
2. AssetBundle into `StreamingAssets/mods/`
3. Tests: solo host `/spawn rocketlauncher` → shoot → explosion kills fish within the radius, boat gets pushed away, multiplayer: remote clients see the projectile (WeaponInfo is just a byte index — the mod must be installed on all clients)

### Rocket Behavior (values in WeaponInfo/Weapon)
- `_projSpeed`: ~35 (slow enough to watch)
- `ProjectileGravity`: ~4–6 (slight arc)
- `_spread`: 0, `_fullAuto`: false, `_recoilKnockback`: high
- `ProjectileType.WidthRadius`: 0.15, `MeshInstance`: "RocketMesh", `MeshScale`: (0.15, 0.15, 0.4)
- Trail: none for MVP; later a custom particle system or the existing "Smoke" effect

---

## Open Points / Decisions
1. **Texture:** get `Palette -Sharks.png`? (otherwise flat colors)
2. **Animation takes:** identify the 40 `CubeAction.NNN` takes in Blender (fire/reload/idle)? — or for MVP just build simple clips ourselves (idle=loop, fire=recoil pose, reload=rocket insert)
3. **Sounds:** reuse the game's sounds (dynamite ignition sound, explosion sound) — no new assets needed
4. **Shop integration:** MVP is `/spawn` only; purchasable in the shop would be step 5 (purchasable system)