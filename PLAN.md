# How to Fish — Custom Weapon Mod: "Asimov Rocket Launcher"

## Analyse-Ergebnisse

### Spiel
- **Engine:** Unity **6000.4.4f1** (Unity 6), URP, FishNet (Multiplayer), Steam
- **Item-Registrierung:** `GameInfo.Awake()` lädt alle Items via `Resources.LoadAll<Item>("Items")` aus `resources.assets` → Name+ID-Dictionary
- **Waffen-Pipeline:** `Weapon` (Tool→Item) → `WeaponInfo` (byte `ProjectileType` = Index in `ProjectileManager._types[]`) → Projektil-Rendering per GPU-Instancing über `InstanceManager` (Name→Mesh+Material, KEIN GameObject)
- **Explosionen:** `ExplosionManager.ServerExplode(Item, ExplosionInfo)` — komplettes System vorhanden (Damage-Radius, Knockback, Fische töten, Boat-Force, Screenshake, VFX/Sound)
- **Cheats:** `/spawn <name>` in Chat — aber `CheatsEnabled` hängt an `SteamManager.IsDev` (hardcoded SteamIDs) → muss im Mod aktiviert werden
- **Build:** dekompiliertes `Assembly-CSharp.csproj` **baut jetzt mit 0 Errors** gegen die Spiel-DLLs (HintPaths gefixt, `UnityEngine.UIElementsModule` ergänzt). DLL-Swap ist also machbar.

### FBX (Asimov Weapon Pack)
- **Enthält KEINE echten Animationen** — alle 37 Takes sind 1-Frame-Rest-Posen (Pose-Library). Animationen müssen selbst gebaut werden (aber das Rig macht's einfach)
- **Rig-Struktur:** nur 3 relevante Bones: `Body` (len 0.71, root), `Trigger` (child von Body), `Rocket` (child von Body, hinten im Rohr)
- **Mesh-Zustände:** `Rocket` (geladene Rakete sichtbar) + `Rocket Fired` (leere Röhre) — ideal für Fire/Reload-Anims
- **Extrahiert:** `/tmp/opencode/fbx_out/AsimovRocketLauncher_clean.fbx` (4 MB, nur Rocket-Launcher-Teile)
- **Materialien:** `GunMetalNewPallete`, `PlasticNewPallete`
- ⚠️ **Textur fehlt:** `Palette -Sharks.png` ist NICHT in der FBX embedded (nur Pfad-Referenz auf `/run/media/Hans/...`). Ohne die PNG wird's untexturiert/weiß. → Bitte die Palette-PNG aus dem Pack besorgen, sonst ersetzen wir sie durch Flat-Colors.

---

## Empfohlener Weg: Modifizierte Assembly-CSharp.dll + AssetBundle

(Voll-Rip mit AssetRipper wäre "sauberer", aber selten 1:1 lauffähig. Hybrid-Ansatz ist roboser und schneller.)

### Schritt 1 — Unity-Editor-Projekt (Unity 6000.4.4f1)
1. Leeres URP-Projekt anlegen
2. `AsimovRocketLauncher_clean.fbx` importieren
3. **Animationen selbst bauen** (Pack hat keine — nur 1-Frame-Rest-Posen). Legacy-Clips mit exakten Namen die `Weapon.cs` erwartet: `Idle`, `Fire`, `FireLast`, `Reload`, `ReloadLast`, `Inspect`. Rig ist minimal (Bones: `Body`, `Trigger`, `Rocket`): `Idle`=Rest-Pose loop, `Fire`=Trigger zurueck + Rocket-Bone scale 0 + Recoil (~10f), `Reload`=Rocket wieder rein (~40f). Export als **Legacy-Animation** (nicht Humanoid), Unity-Import auf "Legacy"
4. `Palette -Sharks.png` (falls vorhanden) importieren, Materialien auf URP umstellen
5. **AssetBundle bauen** (StandaloneLinux64) mit:
   - Launcher-Mesh + Material
   - Rocket-Mesh + Material (für InstanceManager)
   - AnimationClips
   - Optional: eigene Explosion-VFX (sonst nutzen wir die Spiel-eigenen über `ParticleManager`-Namen)

### Schritt 2 — Code-Änderungen im dekompilierten Projekt
Alle Änderungen direkt in Assembly-CSharp (wir besitzen den Code):

1. **`RocketLauncherMod.cs` (neu)** — Bootstrap via `[RuntimeInitializeOnLoadMethod]`:
   - AssetBundle aus `How to Fish_Data/StreamingAssets/mods/` laden
   - Nach `GameInfo.Awake`: neuen **ProjectileType** an `ProjectileManager._types` anhängen (Index = Länge, z.B. 3) — direkter Feld-Zugriff möglich da gleiche Assembly
   - Neuen **InstanceType** `"RocketMesh"` in `InstanceManager` registrieren (Mesh+Material aus Bundle, auch ins `_instanceTypes`-Array + Dict, sonst rendert `RenderBatches()` ihn nicht)
   - Waffen-Prefab: existierendes Waffen-Item aus `GameInfo` klonen (Template), Mesh/Renderer tauschen, WeaponInfo setzen (ProjectileType=neuer Index, Gravity~5 für Bogenschuss, Force), als `"RocketLauncher"` + freie Item-ID (z.B. 200) in `GameInfo` registrieren → `/spawn rocketlauncher` funktioniert dann sofort
   - `ClientSettings.ToggleCheats(true)` im Bootstrap (umgeht `SteamManager.IsDev`)
2. **`ProjectileManager.Hit()`** — Erweiterung:
   ```csharp
   if (projectile.TypeId == RocketLauncherMod.RocketTypeId) {
       ExplosionManager.ServerExplodeAt(hit.point, projectile.Owner, RocketLauncherMod.ExplosionInfo);
       // kein LandProjectile, kein LandedMesh
   }
   ```
3. **`ExplosionManager.cs`** — neue Methode `ServerExplodeAt(Vector3 pos, Player player, ExplosionInfo info)`: Refactor von `ServerExplode` mit `pos` statt `item.transform.position` (Rakete hat kein Item-GameObject). Observer-RPC für VFX/Sound/Screenshake analog `ObserverExplode`.
4. **`Weapon.cs`** — kein Zwang, aber optional: `_projectileCountPerShot=1`, hoher `_recoilKnockback`, `_noShootingDuringShootAnim=true` per Prefab-Konfig im Bootstrap.

### Schritt 3 — ExplosionInfo (im Bootstrap konfigurierbar)
```csharp
Damage: 150 | DamageRadius: 6 | ForceRadius: 4
ItemForce: 1500 | BoatForce: 1500 | PlayerForce: 60
ExplosionParticleName: <vorhandener ParticleManager-Effekt>  // z.B. Dynamite-Explosion
ExplosionSoundName: <vorhandener Sound>                      // z.B. "Explosion_V"
```

### Schritt 4 — Deployment
1. `dotnet build` → neue `Assembly-CSharp.dll` → nach `Managed/` kopieren (Backup original!)
2. AssetBundle nach `StreamingAssets/mods/`
3. Tests: Solo-Host `/spawn rocketlauncher` → Schuss → Explosion tötet Fische im Radius, Boot wird weggeschubst, Multiplayer: Remote-Clients sehen Projektil (WeaponInfo ist nur byte-Index — mod muss auf allen Clients installiert sein)

### Raketen-Verhalten (Werte in WeaponInfo/Weapon)
- `_projSpeed`: ~35 (langsam genug zum Zuschauen)
- `ProjectileGravity`: ~4–6 (leichter Bogenschuss)
- `_spread`: 0, `_fullAuto`: false, `_recoilKnockback`: hoch
- `ProjectileType.WidthRadius`: 0.15, `MeshInstance`: "RocketMesh", `MeshScale`: (0.15, 0.15, 0.4)
- Trail: MVP ohne; später eigenes Partikel-System oder bestehender "Smoke"-Effekt

---

## Offene Punkte / Entscheidungen
1. **Textur:** `Palette -Sharks.png` besorgen? (sonst Flat-Colors)
2. **Animation-Takes:** die 40 `CubeAction.NNN` in Blender identifizieren (Fire/Reload/Idle)? — oder für MVP einfache selbstgebaute Clips (Idle=loop, Fire=Recoil-Pose, Reload=Rocket-Insert)
3. **Sounds:** Spiel-Sounds wiederverwenden (Dynamite-Zündsound, Explosions-Sound) — keine neuen Assets nötig
4. **Shop-Integration:** MVP nur `/spawn`; kaufbar im Shop wäre Schritt 5 (Purchasable-System)