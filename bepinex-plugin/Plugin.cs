using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using HarmonyLib;
using UnityEngine;

namespace RocketLauncherMod
{
	[BepInPlugin("com.kimox.rocketlauncher", "Rocket Launcher Mod", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		public static ManualLogSource Log;

		public const byte ItemId = 200;
		public const string ItemName = "RocketLauncher";
		public const string RocketInstanceName = "RocketMesh";

		public static byte RocketTypeId;
		public static bool RocketTypeReady;
		public static float MaxRocketSpeed = float.MaxValue;
		public static ExplosionInfo ExplosionInfo;
		public static Item RocketLauncherItem;

		private static Mesh _rocketMesh;
		private static Material _rocketMaterial;

		public static ConfigEntry<bool> CfgEnableCheats;
		public static ConfigEntry<bool> CfgKeepTimeScaleNormal;
		public static ConfigEntry<float> CfgLaunchSpeed;
		public static ConfigEntry<float> CfgMaxSpeed;
		public static ConfigEntry<float> CfgAcceleration;
		public static ConfigEntry<float> CfgGravity;
		public static ConfigEntry<float> CfgFuseSeconds;
		public static ConfigEntry<float> CfgTimeBetweenShots;
		public static ConfigEntry<int> CfgAmmoPerMag;
		public static ConfigEntry<float> CfgMeshScale;
		public static ConfigEntry<Vector3> CfgModelPos;
		public static ConfigEntry<Vector3> CfgModelRot;
		public static ConfigEntry<float> CfgModelScale;
		public static ConfigEntry<float> CfgScreenRecoil;
		public static ConfigEntry<float> CfgModelRecoil;
		public static ConfigEntry<int> CfgKnockback;
		public static ConfigEntry<float> CfgReloadSpeed;

		private void Awake()
		{
			Log = Logger;
			CfgEnableCheats = Config.Bind("General", "EnableGameCheats", false,
				"Aktiviert die eingebauten Dev-Cheats des Spiels (/spawn, /money ...). ACHTUNG: schaltet auch die Cheat-Hotkeys frei - T = Zeitlupe (1x/0.1x/0.01x), G = Schaden, H = Heilen, M/N = Geld, O = Insel, U = UI. Der Rocket Launcher braucht das NICHT (/rocket funktioniert immer).");
			CfgKeepTimeScaleNormal = Config.Bind("General", "KeepTimeScaleNormal", true,
				"Setzt Time.timeScale automatisch auf 1 zurueck, falls etwas das Spiel in Zeitlupe schaltet.");
			// Absolute Werte statt Faktoren: Gewehrkugeln fliegen in diesem Spiel 900 m/s.
			// "Etwas langsamer" waere immer noch unsichtbar - eine sichtbare Rakete braucht 40-90 m/s.
			CfgLaunchSpeed = Config.Bind("Rocket", "LaunchSpeed", 40f,
				"Startgeschwindigkeit der Rakete in m/s beim Verlassen des Rohres. (Sturmgewehr-Kugel zum Vergleich: 900 m/s)");
			CfgMaxSpeed = Config.Bind("Rocket", "MaxSpeed", 80f,
				"Endgeschwindigkeit in m/s, auf die der Raketenmotor beschleunigt.");
			CfgAcceleration = Config.Bind("Rocket", "Acceleration", 30f,
				"Schub des Raketenmotors in m/s^2 bis MaxSpeed. 0 = konstante Geschwindigkeit.");
			CfgGravity = Config.Bind("Rocket", "Gravity", 1.5f,
				"Schwerkraft auf die Rakete in m/s^2. 0 = schnurgerade, 1.5 = leichtes Absacken auf Distanz.");
			CfgFuseSeconds = Config.Bind("Rocket", "FuseSeconds", 4f,
				"Nach dieser Flugzeit detoniert die Rakete auch ohne Treffer. Achtung: Geschosse werden 400 m vom Schuetzen entfernt zwangsentfernt - LaunchSpeed x FuseSeconds sollte darunter bleiben.");
			CfgTimeBetweenShots = Config.Bind("Rocket", "TimeBetweenShots", 1.2f,
				"Feuerrate: Sekunden zwischen zwei Schuessen.");
			CfgMeshScale = Config.Bind("Rocket", "MeshScale", 2f,
				"Groesse der fliegenden Rakete. 1 = rocket.obj in Originalgroesse (ca. 30 cm lang).");
			CfgAmmoPerMag = Config.Bind("Rocket", "AmmoPerMag", 1,
				"Schuss pro Magazin. 0 = unendlich Munition (kein Nachladen noetig).");

			// Recoil: ScreenRecoil = Kamera-Kick (Grad nach oben), ModelRecoil = Waffenmodell-Weg.
			// Defaults deutlich ueber Sturmgewehr-Niveau - ein Rueckstoss-Rohr soll spuerbar reinstossen.
			CfgScreenRecoil = Config.Bind("Recoil", "ScreenRecoil", 9f,
				"Kamera-Rueckstoss pro Schuss (Grad). Sturmgewehr liegt bei ca. 1.5.");
			CfgModelRecoil = Config.Bind("Recoil", "ModelRecoil", 4f,
				"Faktor fuer den Rueckstoss des Waffenmodells (ToolMovement.Recoil).");
			CfgKnockback = Config.Bind("Recoil", "Knockback", 25,
				"Spieler-Knockback nach hinten pro Schuss (m/s auf den Rigidbody). Sturmgewehr: ~2.");
			CfgReloadSpeed = Config.Bind("Rocket", "ReloadSpeed", 2f,
				"Geschwindigkeit der Reload-Animation (2 = doppelte Geschwindigkeit, also halbe Reload-Zeit).");

			// Von Hand im Spiel justiert bis die Launcher-Spitze in der vorderen Hand lag
			// (kalibriert gegen die Ruhepose-Matrix in CalculateAnchor/Configure).
			CfgModelPos = Config.Bind("Model", "Position", new Vector3(306.154f, 9.013f, 10.704f),
				"Verschiebung des Launcher-Modells in der Hand (x=rechts, y=hoch, z=vorne), in Metern. Live aenderbar mit /rocketmodel pos X Y Z.");
			CfgModelRot = Config.Bind("Model", "Rotation", new Vector3(0f, 0f, 0f),
				"Zusaetzliche Drehung des Launcher-Modells in Grad. Live aenderbar mit /rocketmodel rot X Y Z.");
			CfgModelScale = Config.Bind("Model", "Scale", 1.5f,
				"Groesse des Launcher-Modells in der Hand. Live aenderbar mit /rocketmodel scale N.");

			HomingMissiles.Bind(Config);
		}

		private void Update()
		{
			if (CfgKeepTimeScaleNormal != null && CfgKeepTimeScaleNormal.Value && Time.timeScale != 1f)
			{
				Log.LogWarning($"Time.timeScale war {Time.timeScale} - wird auf 1 zurueckgesetzt (Zeitlupen-Cheat?)");
				Time.timeScale = 1f;
			}
			EnsureInstanceRegistered();
			EnsureNetworkPrefabRegistered();
			UpdateHomingLock();
		}

		private static void UpdateHomingLock()
		{
			// Reset bei Szenenwechsel/Spiel-Ende: Kein LocalPlayer mehr = Session zu Ende.
			if (Player.LocalPlayer == null && HomingMissiles.Purchased)
			{
				HomingMissiles.Reset();
			}
			bool isAds = false;
			Item held = (Player.LocalPlayer != null && Player.LocalPlayer.Holding != null) ? Player.LocalPlayer.Holding.HeldItem : null;
			if (held != null && held.ID == ItemId && held.Weapon != null && held.Weapon.IsAds)
			{
				isAds = true;
			}
			HomingMissiles.UpdateLocalLock(isAds);
		}

		private void LateUpdate()
		{
			if (!RocketTypeReady || ProjectileManager.Instance == null)
			{
				return;
			}
			RocketVisuals.Sync(ProjectileManager.Instance.GetType(RocketTypeId).Projectiles, Vector3.one * CfgMeshScale.Value);
		}

		/// <summary>
		/// InstanceManager.Awake() baut sein Dictionary bei jedem Szenenwechsel neu aus _instanceTypes auf.
		/// Ohne erneute Registrierung waere der Raketen-Mesh-Typ danach weg (und ReplaceBatches wuerde werfen).
		/// </summary>
		public static bool EnsureInstanceRegistered()
		{
			if (_rocketMesh == null || _rocketMaterial == null)
			{
				return false;
			}
			InstanceManager instanceManager = GetInstanceManager();
			if (instanceManager == null)
			{
				return false;
			}
			if (GetRegisteredInstance(RocketInstanceName) != null)
			{
				return true;
			}
			AddInstanceType(instanceManager, _rocketMesh, _rocketMaterial);
			Log.LogInfo("Raketen-Mesh neu registriert (Szenenwechsel)");
			return true;
		}

		private static InstanceManager _instanceManager;

		private static InstanceManager GetInstanceManager()
		{
			// Beim Szenenwechsel wird das alte Objekt zerstoert - Unity meldet es dann als == null.
			if (_instanceManager == null)
			{
				_instanceManager = UnityEngine.Object.FindObjectOfType<InstanceManager>();
			}
			return _instanceManager;
		}

		private static readonly FieldInfo _instanceTypeDicField = AccessTools.Field(typeof(InstanceManager), "_instanceTypeDic");

		/// <summary>Eintrag aus dem privaten statischen _instanceTypeDic, oder null.</summary>
		private static object GetRegisteredInstance(string name)
		{
			IDictionary dic = (IDictionary)_instanceTypeDicField.GetValue(null);
			return (dic != null && dic.Contains(name)) ? dic[name] : null;
		}

		private static void AddInstanceType(InstanceManager instanceManager, Mesh mesh, Material material)
		{
			Type nestedType = AccessTools.Inner(typeof(InstanceManager), "InstanceType");
			object instanceType = Activator.CreateInstance(nestedType);
			AccessTools.Field(nestedType, "Name").SetValue(instanceType, RocketInstanceName);
			AccessTools.Field(nestedType, "Mesh").SetValue(instanceType, mesh);
			AccessTools.Field(nestedType, "Material").SetValue(instanceType, material);
			AccessTools.Field(nestedType, "Batches").SetValue(instanceType, new List<List<Matrix4x4>> { new List<Matrix4x4>() });

			IDictionary dic = (IDictionary)_instanceTypeDicField.GetValue(null);
			dic[RocketInstanceName] = instanceType;

			FieldInfo typesField = AccessTools.Field(typeof(InstanceManager), "_instanceTypes");
			Array types = (Array)typesField.GetValue(instanceManager);
			Array newTypes = Array.CreateInstance(nestedType, types.Length + 1);
			Array.Copy(types, newTypes, types.Length);
			newTypes.SetValue(instanceType, types.Length);
			typesField.SetValue(instanceManager, newTypes);
		}

		private IEnumerator Start()
		{
			Log.LogInfo("RocketLauncherMod starting...");
			try
			{
				new Harmony("com.kimox.rocketlauncher").PatchAll(typeof(Patches));
			}
			catch (Exception ex)
			{
				Log.LogError("Harmony-Patches fehlgeschlagen: " + ex);
				yield break;
			}
			while (!ProjectileManager.Instance || !GameInfo.CurCamera)
			{
				Patches.EnableCheats();
				yield return null;
			}
			yield return new WaitForSeconds(1f);
			try
			{
				InitInternal();
			}
			catch (Exception ex)
			{
				Log.LogError("Init failed: " + ex);
			}
		}

		private void InitInternal()
		{
			ExplosionInfo = BuildExplosionInfo();
			RocketTypeId = RegisterProjectileType();
			RegisterRocketInstance();
			BuildWeapon();
			RocketTypeReady = true;
			Log.LogInfo("Initialized. RocketTypeId=" + RocketTypeId);
			ChatManager.ChatMessage("[RocketLauncherMod] ready! Use /rocket to spawn the Rocket Launcher");
		}

		private string AssetDir => Path.Combine(Application.streamingAssetsPath, "mods");

		private ExplosionInfo BuildExplosionInfo()
		{
			ExplosionInfo explosionInfo = new ExplosionInfo();
			Item spawnable = GameInfo.GetSpawnable("dynamite");
			if ((bool)spawnable && spawnable.GetExplosionInfo() != null)
			{
				ExplosionInfo explosionInfo2 = spawnable.GetExplosionInfo();
				SetField(explosionInfo, "_damage", explosionInfo2.Damage);
				SetField(explosionInfo, "damageRadius", explosionInfo2.DamageRadius);
				SetField(explosionInfo, "_forceRadius", explosionInfo2.ForceRadius);
				SetField(explosionInfo, "_itemForce", explosionInfo2.ItemForce);
				SetField(explosionInfo, "_boatForce", explosionInfo2.BoatForce);
				SetField(explosionInfo, "_playerForce", explosionInfo2.PlayerForce);
				SetField(explosionInfo, "_explosionParticleName", explosionInfo2.ExplosionParticleName);
				SetField(explosionInfo, "_explosionSoundName", explosionInfo2.ExplosionSoundName);
				SetField(explosionInfo, "_explosionSounds", explosionInfo2.ExplosionSounds);
				SetField(explosionInfo, "_explosionSoundVol", explosionInfo2.ExplosionSoundVol);
				SetField(explosionInfo, "_screenShakeAmount", explosionInfo2.ScreenShakeAmount);
				SetField(explosionInfo, "_hasUnderwaterExplosion", explosionInfo2.HasUnderwaterExplosion);
				Log.LogInfo("ExplosionInfo copied from dynamite");
			}
			else
			{
				SetField(explosionInfo, "_damage", 150);
				SetField(explosionInfo, "damageRadius", 6f);
				SetField(explosionInfo, "_forceRadius", 4f);
				SetField(explosionInfo, "_itemForce", 1500f);
				SetField(explosionInfo, "_boatForce", 1500f);
				SetField(explosionInfo, "_playerForce", 60f);
				SetField(explosionInfo, "_explosionParticleName", "Explosion");
				SetField(explosionInfo, "_explosionSoundName", "Explosion_V");
				SetField(explosionInfo, "_explosionSounds", 0);
				SetField(explosionInfo, "_explosionSoundVol", 1f);
				SetField(explosionInfo, "_screenShakeAmount", 1000f);
				Log.LogWarning("dynamite not found, using default ExplosionInfo");
			}
			return explosionInfo;
		}

		private byte RegisterProjectileType()
		{
			ProjectileType projectileType = new ProjectileType();
			projectileType.WidthRadius = 0.15f;
			projectileType.IsHitScan = false;
			projectileType.PlayerForce = 100f;
			projectileType.MeshInstance = RocketInstanceName;
			projectileType.MeshScale = Vector3.one * CfgMeshScale.Value;
			FieldInfo field = AccessTools.Field(typeof(ProjectileManager), "_types");
			ProjectileType[] array = (ProjectileType[])field.GetValue(ProjectileManager.Instance);
			ProjectileType[] array2 = new ProjectileType[array.Length + 1];
			Array.Copy(array, array2, array.Length);
			byte result = (byte)array.Length;
			array2[array.Length] = projectileType;
			field.SetValue(ProjectileManager.Instance, array2);
			Log.LogInfo("ProjectileType registered at index " + result);
			return result;
		}

		private void RegisterRocketInstance()
		{
			// rotateX=0: die lange Achse der OBJ liegt auf +Z - genau die Achse, die
			// MatrixFromProjectile per LookRotation in Flugrichtung dreht.
			_rocketMesh = ObjLoader.LoadMesh(Path.Combine(AssetDir, "rocket.obj"), 0.1f, 0f);
			if (_rocketMesh == null)
			{
				throw new Exception("rocket.obj could not be loaded from " + AssetDir);
			}
			_rocketMaterial = CreateMaterial(LoadTexture(Path.Combine(AssetDir, "palette-sharks.png")));
			InstanceManager instanceManager = GetInstanceManager();
			if (instanceManager == null)
			{
				throw new Exception("InstanceManager not found in scene");
			}
			// Der Instanz-Typ bleibt nur als Platzhalter registriert, damit Vanilla-Code nicht ins Leere greift.
			// Gezeichnet wird die Rakete von RocketVisuals als echtes GameObject - GPU-Instancing mit dem
			// Geschoss-Material des Spiels hat nichts sichtbar gemacht (Kugeln fliegen 900 m/s, da faellt es nicht auf).
			AddInstanceType(instanceManager, _rocketMesh, _rocketMaterial);
			RocketVisuals.Setup(_rocketMesh, _rocketMaterial);
			Log.LogInfo($"Rocket-Mesh geladen (verts: {_rocketMesh.vertexCount}, bounds: {_rocketMesh.bounds.size}, shader: {_rocketMaterial.shader.name})");
		}


		private void BuildWeapon()
		{
			Item spawnable = GameInfo.GetSpawnable("assaultrifle");
			if (!spawnable)
			{
				throw new Exception("assaultrifle template not found");
			}
			GameObject gameObject = UnityEngine.Object.Instantiate(spawnable.gameObject);
			gameObject.name = ItemName;
			gameObject.transform.position = Vector3.down * 1000f;
			Weapon component = gameObject.GetComponent<Weapon>();
			if (component == null)
			{
				throw new Exception("template has no Weapon component");
			}
			ConfigureWeapon(component);
			ApplyReloadSpeed(component);
			SwapModel(component);
			SetItemID(gameObject.GetComponent<Item>(), ItemId);
			RocketLauncherItem = gameObject.GetComponent<Item>();
			RegisterSpawnable(RocketLauncherItem);
			DontDestroyOnLoad(gameObject);
			EnsureNetworkPrefabRegistered();
			Log.LogInfo("RocketLauncher registered: /spawn rocketlauncher");
		}

		/// <summary>
		/// Eigene FishNet-Prefab-Collection fuer Mod-Items. Muss != 0 sein (0 ist die im
		/// NetworkManager eingebackene Vanilla-Liste) und auf Host wie Client identisch.
		/// Eine eigene Collection ist der Vanilla-Liste vorzuziehen: dort waere die PrefabId
		/// der aktuelle Listen-Index und damit davon abhaengig, dass beide Seiten exakt
		/// gleich viele Prefabs geladen haben. Hier ist sie immer 0.
		/// </summary>
		public const ushort NetworkCollectionId = 20200;

		private static NetworkManager _registeredWith;

		/// <summary>
		/// Ohne das hier ist der Launcher nicht multiplayer-tauglich: Das Template ist ein
		/// Laufzeit-Klon des Sturmgewehr-Prefabs und traegt dessen serialisierte
		/// NetworkObject.PrefabId weiter. FishNet schreibt beim Spawn nur diese ID ins Paket
		/// (ManagedObjects.WriteSpawn) und der Client schlaegt sie in seiner Prefab-Liste nach
		/// (ClientObjects.GetInstantiatedNetworkObject) - er instanziiert also das VANILLA-
		/// Sturmgewehr. ID 200, Modell, Waffenwerte und ProjectileType leben nur auf dem Klon
		/// der Maschine, die ihn gebaut hat.
		///
		/// AddObject setzt via InitializePrefabRange PrefabId und SpawnableCollectionId auf
		/// dem Template; beide werden im Spawn-Paket uebertragen (WriteSpawnedNetworkObject
		/// schreibt die CollectionId). Ein Client mit Mod hat dieselbe Collection registriert
		/// und instanziiert damit sein eigenes Launcher-Template.
		///
		/// Idempotent: checkForDuplicates verhindert Doppel-Eintraege, deshalb kann das aus
		/// Update() gegen einen NetworkManager-Wechsel beim Szenenwechsel laufen.
		/// </summary>
		public static bool EnsureNetworkPrefabRegistered()
		{
			if (RocketLauncherItem == null)
			{
				return false;
			}
			NetworkManager manager = InstanceFinder.NetworkManager;
			if (manager == null)
			{
				return false;
			}
			NetworkObject nob = RocketLauncherItem.GetComponent<NetworkObject>();
			if (nob == null)
			{
				return false;
			}
			// Registrierung gilt, solange derselbe NetworkManager die Collection noch fuehrt.
			if (_registeredWith == manager
				&& nob.SpawnableCollectionId == NetworkCollectionId
				&& nob.PrefabId != ushort.MaxValue
				&& manager.GetPrefabObjects<SinglePrefabObjects>(NetworkCollectionId, createIfMissing: false) != null)
			{
				return true;
			}
			PrefabObjects prefabs = manager.GetPrefabObjects<SinglePrefabObjects>(NetworkCollectionId, createIfMissing: true);
			if (prefabs == null)
			{
				Log.LogError($"FishNet-Collection {NetworkCollectionId} konnte nicht angelegt werden - Launcher bleibt Singleplayer-only.");
				return false;
			}
			// Laufzeit-ScriptableObject: ohne dieses Flag kann Resources.UnloadUnusedAssets
			// die Collection beim Szenenwechsel wegraeumen, obwohl der NetworkManager sie
			// in _runtimeSpawnablePrefabs noch referenziert.
			prefabs.hideFlags |= HideFlags.DontUnloadUnusedAsset;
			prefabs.AddObject(nob, checkForDuplicates: true);
			_registeredWith = manager;
			Log.LogInfo($"Netzwerk-Prefab registriert: CollectionId {nob.SpawnableCollectionId}, PrefabId {nob.PrefabId} (vorher Sturmgewehr-PrefabId - deshalb kam beim Mitspieler ein Sturmgewehr an)");
			return true;
		}

		private void ConfigureWeapon(Weapon weapon)
		{
			WeaponInfo weaponInfo = (WeaponInfo)AccessTools.Field(typeof(Weapon), "_weaponInfo").GetValue(weapon);
			weaponInfo.ProjectileType = RocketTypeId;
			weaponInfo.ProjectileDamage = 150;
			weaponInfo.ProjectileForce = 50f;
			weaponInfo.ProjectileGravity = CfgGravity.Value;
			weaponInfo.ShootVFX = "";
			weaponInfo.BoatForceOverride = 800f;
			SetField(weapon, "_timeBetweenShots", CfgTimeBetweenShots.Value);
			SetField(weapon, "_spread", 0f);
			SetField(weapon, "_fullAuto", false);
			SetField(weapon, "_recoilKnockback", CfgKnockback.Value);
			SetField(weapon, "_projectileCountPerShot", 1);
			SetField(weapon, "_noShootingDuringShootAnim", true);
			// ADS frei geben - noetig fuer das Homing-Lock (Lock-Box erscheint beim Zielen).
			SetField(weapon, "_canAds", true);
			float bulletSpeed = (float)AccessTools.Field(typeof(Weapon), "_projSpeed").GetValue(weapon);
			float launchSpeed = Mathf.Max(1f, CfgLaunchSpeed.Value);
			MaxRocketSpeed = Mathf.Max(launchSpeed, CfgMaxSpeed.Value);
			SetField(weapon, "_projSpeed", launchSpeed);
			Log.LogInfo($"Rocket: start {launchSpeed:0.#} m/s -> max {MaxRocketSpeed:0.#} m/s (Sturmgewehr-Kugel: {bulletSpeed:0.#} m/s), schub {CfgAcceleration.Value}, gravity {CfgGravity.Value}, fuse {CfgFuseSeconds.Value}s, Flugweite ca. {Mathf.Min(launchSpeed * CfgFuseSeconds.Value * 1.6f, 400f):0}m");
			Attachments component = weapon.GetComponent<Attachments>();
			ApplyAmmo(component, weapon);
			ApplyRecoil(weapon);
		}

		/// <summary>AmmoPerMag anwenden. 0 = unendlich: intern wird ein großes Magazin (999)
		/// gesetzt, denn AmmoPerMag=0 selbst waere eine Sackgasse (Ammo=0 => Shoot bricht ab,
		/// Reload fuellt auf 0). Zusammen mit dem Instant-Reload-Postfix ergibt das Dauer-Feuer.</summary>
		public const int InfiniteAmmoInternal = 999;

		public static void ApplyAmmo(Attachments attachments, Weapon weapon)
		{
			if (attachments == null)
			{
				return;
			}
			int cfg = CfgAmmoPerMag.Value;
			int ammo = (cfg <= 0) ? InfiniteAmmoInternal : cfg;
			SetField(attachments, "_defaultAmmoPerMag", ammo);
			SetField(attachments, "_extendedAmmoPerMag", ammo);
			if (weapon != null)
			{
				AccessTools.Property(typeof(Weapon), "Ammo").GetSetMethod(nonPublic: true).Invoke(weapon, new object[1] { ammo });
			}
		}

		/// <summary>
		/// Rueckstoss: ScreenRecoil/ModelRecoil skalieren die BarrelAttachment-Werte des
		/// Sturmgewehrs. Default 1.0 ist Vanille-Gewehr - hoeher = harter Kick.
		/// Angewendet auf ALLE Barrels der Waffe, denn Attachments greift ueber den
		/// SyncVar-Index _syncedBarrelAttachment zu - welcher Barrel aktiv ist, kann wechseln.
		/// </summary>
		public static void ApplyRecoil(Weapon weapon)
		{
			if (weapon == null)
			{
				return;
			}
			Attachments attachments = weapon.GetComponent<Attachments>();
			if (attachments == null)
			{
				return;
			}
			// _barrelAttachments ist eine List<BarrelAttachment>, kein einzelnes Attachment.
			List<BarrelAttachment> barrels = AccessTools.Field(typeof(Attachments), "_barrelAttachments").GetValue(attachments) as List<BarrelAttachment>;
			if (barrels == null || barrels.Count == 0)
			{
				return;
			}
			CaptureVanillaRecoil(barrels);
			for (int i = 0; i < barrels.Count && i < _vanillaScreenRecoil.Length; i++)
			{
				if (barrels[i] == null)
				{
					continue;
				}
				SetField(barrels[i], "_screenRecoilAmount", _vanillaScreenRecoil[i] * CfgScreenRecoil.Value);
				SetField(barrels[i], "_weaponRecoilMulti", _vanillaWeaponRecoilMulti[i] * CfgModelRecoil.Value);
			}
			SetField(weapon, "_recoilKnockback", CfgKnockback.Value);
		}

		/// <summary>
		/// Die Vanilla-Werte genau einmal sichern - beim allerersten Aufruf, der immer auf dem
		/// frisch geklonten Template laeuft (ConfigureWeapon in BuildWeapon). Pro Barrel-INDEX,
		/// nicht pro Instanz: jeder gespawnte Launcher klont die bereits multiplizierten Werte
		/// des Templates. Wuerden wir die als Ausgangswert nehmen, potenzierte sich der
		/// Rueckstoss mit jedem /rocketcfg recoil.
		/// </summary>
		private static void CaptureVanillaRecoil(List<BarrelAttachment> barrels)
		{
			if (_vanillaScreenRecoil != null)
			{
				return;
			}
			_vanillaScreenRecoil = new Vector2[barrels.Count];
			_vanillaWeaponRecoilMulti = new float[barrels.Count];
			for (int i = 0; i < barrels.Count; i++)
			{
				if (barrels[i] == null)
				{
					continue;
				}
				_vanillaScreenRecoil[i] = barrels[i].ScreenRecoilAmount;
				_vanillaWeaponRecoilMulti[i] = barrels[i].WeaponRecoilMulti;
			}
			Log.LogInfo($"Vanilla-Rueckstoss gesichert ({barrels.Count} Barrel(s)): screen {_vanillaScreenRecoil[0]}, modelMulti {_vanillaWeaponRecoilMulti[0]}");
		}

		private static Vector2[] _vanillaScreenRecoil;
		private static float[] _vanillaWeaponRecoilMulti;

		/// <summary>Reload-Animationsgeschwindigkeit setzen (Legacy-Animation, per AnimationState.speed).
		/// ReloadSpeed 0 = Instant-Reload: Nach jedem Schuss ist das Magazin sofort wieder voll.</summary>
		public static void ApplyReloadSpeed(Weapon weapon)
		{
			if (CfgReloadSpeed.Value <= 0f)
			{
				return;
			}
			Animation anim = (Animation)AccessTools.Field(typeof(Tool), "_anim").GetValue(weapon);
			if (anim == null)
			{
				return;
			}
			foreach (string clipName in new string[2] { "Reload", "ReloadLast" })
			{
				AnimationState state = anim[clipName];
				if (state != null)
				{
					state.speed = CfgReloadSpeed.Value;
				}
			}
		}

		private void SwapModel(Weapon weapon)
		{
			// rotateX=0: die lange Achse der OBJ bleibt auf +Z - die Blickrichtung der Waffe in Unity.
			// Mit den alten 270 Grad stand der 65 cm lange Launcher senkrecht vor der Kamera.
			LauncherMesh = ObjLoader.LoadMesh(Path.Combine(AssetDir, "rocketlauncher_body.obj"), 0.1f, 0f);
			if (LauncherMesh == null)
			{
				throw new Exception("rocketlauncher_body.obj could not be loaded");
			}
			Material material = CreateMaterial(LoadTexture(Path.Combine(AssetDir, "palette-sharks.png")));
			Renderer handsMesh = weapon.HandsMesh;
			ReferenceRenderer = null;
			float biggest = 0f;
			foreach (Renderer renderer in weapon.GetComponentsInChildren<Renderer>(includeInactive: true))
			{
				if (renderer == handsMesh)
				{
					continue;
				}
				// Groesster echter Mesh-Renderer = das Sturmgewehr selbst: unsere Positions-Referenz.
				Mesh refMesh = (renderer as SkinnedMeshRenderer)?.sharedMesh;
				if (refMesh == null && renderer.TryGetComponent(out MeshFilter filter))
				{
					refMesh = filter.sharedMesh;
				}
				if (refMesh != null)
				{
					float volume = refMesh.bounds.size.x * refMesh.bounds.size.y * refMesh.bounds.size.z;
					if (volume > biggest)
					{
						biggest = volume;
						ReferenceRenderer = renderer;
						ReferenceBounds = refMesh.bounds;
					}
				}
				renderer.enabled = false;
			}
			GameObject modelObject = new GameObject(ModelObjectName);
			// Modell bleibt Kind der Waffenwurzel: damit ist es in der Ego-Ansicht sichtbar
			// und existiert auch am gedroppten Item (die Rifle-Rig-Hierarchie wird beim
			// Droppen/deaktiviert - ein Modell darunter wuerde verschwinden).
			modelObject.transform.SetParent(weapon.transform, worldPositionStays: false);
			modelObject.AddComponent<MeshFilter>().sharedMesh = LauncherMesh;
			modelObject.AddComponent<MeshRenderer>().sharedMaterial = material;
			ModelTransform = modelObject.transform;
			// LauncherGlue klebt das Modell an Item._handModelRight - das animierte IK-Ziel der
			// rechten Hand (PlayerHands.SyncHiddenHandsToAnimatedTool setzt die Hand-Bones
			// genau dorthin). Dieses Transform ist ein normales Child des Items, wird von der
			// kompletten Bewegungskette bewegt (Sway/Bob/Recoil/ADS + Feuer-/Reload-Rig) und
			// hat keine SkinnedMesh-Bindpose-Skalierung. Bones, Renderer-Transform und
			// SwayTransform haben sich alle als Anker unbrauchbar erwiesen.
			modelObject.AddComponent<LauncherGlue>();
			Transform handAnchor = (Transform)AccessTools.Field(typeof(Item), "_handModelRight").GetValue(weapon.GetComponent<Item>());
			CalculateAnchor(weapon.transform, handAnchor);
			ApplyModelTransform();
			Log.LogInfo($"Weapon model swapped: launcher mesh ({LauncherMesh.vertexCount} verts, bounds {LauncherMesh.bounds.size}), Referenz-Renderer: {(ReferenceRenderer != null ? ReferenceRenderer.gameObject.name : "keiner")}");
		}

		public const string ModelObjectName = "ModModel";

		public static Transform ModelTransform;
		public static Mesh LauncherMesh;
		public static Renderer ReferenceRenderer;
		public static Bounds ReferenceBounds;

		/// <summary>Position des Original-Gewehrmittelpunkts im lokalen Raum der Waffe.</summary>
		private static Vector3 _anchorLocal;

		/// <summary>Abbildung lokaler Waffenwurzel-Raum -> Hand-Anker-Raum, gemessen in der
		/// Ruhepose beim Bau des Templates. Gilt fuer alle Exemplare, weil Spawn-Kopien
		/// dieselbe Ausgangshierarchie besitzen.</summary>
		private static Matrix4x4 _rootToAnchorRest = Matrix4x4.identity;

		private static void CalculateAnchor(Transform weaponRoot, Transform handAnchor)
		{
			_anchorLocal = (ReferenceRenderer != null)
				? weaponRoot.InverseTransformPoint(ReferenceRenderer.transform.TransformPoint(ReferenceBounds.center))
				: Vector3.zero;
			if (handAnchor != null)
			{
				_rootToAnchorRest = weaponRoot.worldToLocalMatrix * handAnchor.localToWorldMatrix;
			}
		}

		/// <summary>
		/// Setzt das Launcher-Modell dorthin, wo vorher das Sturmgewehr sass, plus Feinjustierung
		/// aus der Config. Betrifft ALLE Exemplare - das Template und jeden gespawnten Launcher,
		/// denn beim Spawnen wird das Modell mitkopiert und lebt danach unabhaengig weiter.
		/// </summary>
		public static void ApplyModelTransform()
		{
			if (LauncherMesh == null)
			{
				return;
			}
			GetModelPose(out Vector3 position, out Quaternion rotation, out float scale);
			int count = 0;
			foreach (MeshFilter filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
			{
				if (filter.sharedMesh != LauncherMesh || filter.gameObject.name != ModelObjectName)
				{
					continue;
				}
				LauncherGlue glue = filter.GetComponent<LauncherGlue>() ?? filter.gameObject.AddComponent<LauncherGlue>();
				glue.Configure(position, rotation, scale, _rootToAnchorRest);
				count++;
			}
			if (count > 0)
			{
				Log.LogInfo($"Launcher-Modell aktualisiert ({count} Exemplare): pos {position}, scale {scale}");
			}
		}

		/// <summary>Modell-Pose aus der Config, im lokalen Raum der Waffenwurzel.</summary>
		private static void GetModelPose(out Vector3 position, out Quaternion rotation, out float scale)
		{
			scale = Mathf.Max(0.01f, CfgModelScale.Value);
			rotation = Quaternion.Euler(CfgModelRot.Value);
			position = _anchorLocal - rotation * (LauncherMesh.bounds.center * scale) + CfgModelPos.Value;
		}

		/// <summary>
		/// Klebt das Launcher-Modell an Item._handModelRight des eigenen Exemplars - das
		/// animierte IK-Ziel der rechten Hand (PlayerHands.SyncHiddenHandsToAnimatedTool
		/// setzt die Hand-Bones jeden LateUpdate genau dorthin, Tool.TryActivateAnimatedHands
		/// nutzt es als IK-Ziel). Damit folgt der Launcher der kompletten Bewegungskette:
		/// Sway/Bob/Recoil/ADS ueber die Tool-Hierarchie plus Feuer-/Reload-Animationen des
		/// Rigs. Das Modell selbst bleibt Kind der Waffenwurzel (bleibt also gedroppt
		/// sichtbar) und wird nur jede LateUpdate auf die Anker-Pose plus Offset gesetzt.
		/// </summary>
		private class LauncherGlue : MonoBehaviour
		{
			private Transform _anchor;

			private Vector3 _anchorLocalPos;

			private Quaternion _anchorLocalRot;

			private Vector3 _localScale = Vector3.one;

			/// <summary>True sobald Configure einmal gelaufen ist.</summary>
			public bool IsConfigured { get; private set; }

			/// <summary>
			/// Ein geklontes Exemplar - durch /rocket beim Host, durch den FishNet-Spawn beim
			/// Mitspieler - traegt zwar diese Komponente, aber nicht ihre privaten Felder:
			/// Unity kopiert beim Instantiate nur serialisierten Zustand. Es zieht sich die
			/// Pose deshalb hier selbst. Vorher suchte Update() dafuer jeden Frame per
			/// Resources.FindObjectsOfTypeAll die komplette Szene ab.
			/// </summary>
			private void Awake()
			{
				if (!IsConfigured && LauncherMesh != null)
				{
					GetModelPose(out Vector3 position, out Quaternion rotation, out float scale);
					Configure(position, rotation, scale, _rootToAnchorRest);
				}
			}

			/// <summary>Offset im lokalen Raum der Waffenwurzel (so kommen die Werte aus /rocketmodel);
			/// wird hier gegen die Ruhepose in den Anker-Raum des eigenen Exemplars umgerechnet.</summary>
			public void Configure(Vector3 rootLocalPos, Quaternion rootLocalRot, float scale, Matrix4x4 rootToAnchorRest)
			{
				_anchorLocalRot = Quaternion.Inverse(rootToAnchorRest.rotation) * rootLocalRot;
				_anchorLocalPos = rootToAnchorRest.MultiplyPoint3x4(rootLocalPos);
				_localScale = Vector3.one * scale;
				IsConfigured = true;
				Apply();
			}

			private void LateUpdate()
			{
				Apply();
			}

			private void Apply()
			{
				if (_anchor == null)
				{
					FindAnchor();
					if (_anchor == null)
					{
						return;
					}
				}
				transform.SetPositionAndRotation(
					_anchor.TransformPoint(_anchorLocalPos),
					_anchor.rotation * _anchorLocalRot);
				transform.localScale = _localScale;
			}

			/// <summary>Den Hand-Anker des eigenen Exemplars suchen: vom Modell die Hirarchie
			/// hinauf zum Weapon-Component, dort das private _handModelRight ziehen. Das
			/// Template ist inaktiv - sein LateUpdate laeuft nie, die Suche startet erst in
			/// aktiven Exemplaren.</summary>
			private void FindAnchor()
			{
				Transform current = transform.parent;
				while (current != null)
				{
					Weapon weapon = current.GetComponent<Weapon>();
					if (weapon != null)
					{
						_anchor = (Transform)AccessTools.Field(typeof(Item), "_handModelRight").GetValue(weapon.GetComponent<Item>());
						return;
					}
					current = current.parent;
				}
			}
		}

		private void RegisterSpawnable(Item item)
		{
			string key = item.name.Replace(" ", string.Empty).ToLower();
			Dictionary<string, Item> dictionary = (Dictionary<string, Item>)AccessTools.Field(typeof(GameInfo), "_nameToSpawnable").GetValue(null);
			Dictionary<byte, Item> dictionary2 = (Dictionary<byte, Item>)AccessTools.Field(typeof(GameInfo), "_idToSpawnable").GetValue(null);
			Dictionary<byte, Item> dictionary3 = (Dictionary<byte, Item>)AccessTools.Field(typeof(GameInfo), "_allItems").GetValue(null);
			dictionary[key] = item;
			dictionary2[ItemId] = item;
			dictionary3[ItemId] = item;
		}

		private void SetItemID(Item item, byte id)
		{
			AccessTools.Field(typeof(Item), "_id").SetValue(item, id);
		}

		private Texture2D LoadTexture(string path)
		{
			if (!File.Exists(path))
			{
				Log.LogWarning("Missing texture: " + path);
				return Texture2D.whiteTexture;
			}
			Texture2D texture2D = new Texture2D(2, 2);
			if (!texture2D.LoadImage(File.ReadAllBytes(path)))
			{
				UnityEngine.Object.Destroy(texture2D);
				return Texture2D.whiteTexture;
			}
			texture2D.filterMode = FilterMode.Point;
			texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: true);
			return texture2D;
		}

		private Material CreateMaterial(Texture texture)
		{
			Material material = null;
			Item spawnable = GameInfo.GetSpawnable("assaultrifle");
			if (spawnable != null)
			{
				Renderer[] componentsInChildren = spawnable.GetComponentsInChildren<Renderer>(includeInactive: true);
				for (int i = 0; i < componentsInChildren.Length; i++)
				{
					if (componentsInChildren[i].sharedMaterial != null && componentsInChildren[i].sharedMaterial.shader.name.Contains("Universal Render Pipeline"))
					{
						material = UnityEngine.Object.Instantiate(componentsInChildren[i].sharedMaterial);
						break;
					}
				}
			}
			if (material == null)
			{
				Shader shader = Shader.Find("Universal Render Pipeline/Lit");
				if (shader == null)
				{
					shader = Shader.Find("Standard");
				}
				if (shader == null)
				{
					shader = Shader.Find("Mobile/Diffuse");
				}
				material = new Material(shader);
			}
			if (texture != null)
			{
				if (material.HasProperty("_BaseMap"))
				{
					material.SetTexture("_BaseMap", texture);
				}
				if (material.HasProperty("_MainTex"))
				{
					material.SetTexture("_MainTex", texture);
				}
			}
			// InstanceManager.RenderBatches() zeichnet jeden registrierten Instanz-Typ per
			// DrawMeshInstanced - ohne dieses Flag wirft das jeden Frame eine Exception.
			material.enableInstancing = true;
			return material;
		}

		private static void SetField(object obj, string field, object value)
		{
			AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
		}
	}

	/// <summary>
	/// Zeichnet jede fliegende Rakete als eigenes GameObject. Das Spiel rendert Geschosse per
	/// Graphics.DrawMeshInstanced - bei einer 45 cm langen Rakete ist ein normaler MeshRenderer
	/// verlaesslicher (und erlaubt spaeter Rauchfahne/Licht als Kind-Objekte).
	/// </summary>
	public static class RocketVisuals
	{
		private static Mesh _mesh;
		private static Material _material;
		private static readonly Dictionary<Projectile, GameObject> _live = new Dictionary<Projectile, GameObject>();
		private static readonly List<Projectile> _finished = new List<Projectile>();
		private static readonly Stack<GameObject> _pool = new Stack<GameObject>();

		public static void Setup(Mesh mesh, Material material)
		{
			_mesh = mesh;
			_material = material;
		}

		public static void Sync(List<Projectile> projectiles, Vector3 scale)
		{
			if (_mesh == null || _material == null)
			{
				return;
			}
			// Gleiche Interpolation wie im Spiel: die Positionen kommen aus FixedUpdate.
			float t = (Time.fixedDeltaTime > 0f) ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1f;
			foreach (Projectile projectile in projectiles)
			{
				if (!_live.TryGetValue(projectile, out GameObject visual) || visual == null)
				{
					visual = Rent();
					_live[projectile] = visual;
				}
				Vector3 pos = Vector3.Lerp(projectile.PreviousPosition, projectile.Position, t);
				Vector3 forward = Vector3.Lerp(projectile.PreviousVelocity, projectile.Velocity, t);
				visual.transform.SetPositionAndRotation(pos, (forward.sqrMagnitude > 0.0001f) ? Quaternion.LookRotation(forward) : Quaternion.identity);
				visual.transform.localScale = scale;
			}
			// Kein Count-Vergleich als Abkuerzung: detoniert in einem Frame eine Rakete waehrend
			// eine neue startet, sind die Counts gleich - der Eintrag der toten Rakete bliebe
			// samt sichtbarem GameObject fuer immer stehen. Bei einer Handvoll Raketen ist der
			// Durchlauf ohnehin gratis.
			_finished.Clear();
			foreach (KeyValuePair<Projectile, GameObject> pair in _live)
			{
				if (!projectiles.Contains(pair.Key))
				{
					_finished.Add(pair.Key);
				}
			}
			foreach (Projectile projectile in _finished)
			{
				Release(_live[projectile]);
				_live.Remove(projectile);
			}
		}

		private static GameObject Rent()
		{
			while (_pool.Count > 0)
			{
				GameObject pooled = _pool.Pop();
				if (pooled != null)
				{
					pooled.SetActive(true);
					return pooled;
				}
			}
			GameObject visual = new GameObject("ModRocket");
			UnityEngine.Object.DontDestroyOnLoad(visual);
			visual.AddComponent<MeshFilter>().sharedMesh = _mesh;
			visual.AddComponent<MeshRenderer>().sharedMaterial = _material;
			return visual;
		}

		private static void Release(GameObject visual)
		{
			if (visual == null)
			{
				return;
			}
			visual.SetActive(false);
			_pool.Push(visual);
		}
	}

	public static class Patches
	{
		[HarmonyPatch(typeof(ProjectileManager), "Hit")]
		[HarmonyPrefix]
		public static bool ProjectileManager_Hit_Prefix(ProjectileManager __instance, Projectile projectile, ProjectileType type, RaycastHit hit)
		{
			if (projectile.TypeId != Plugin.RocketTypeId)
			{
				return true;
			}
			if (projectile.IsLocal && !hit.transform.CompareTag("Level"))
			{
				Server.Instance.ProjectileHitDynamic(projectile.Owner.Owner, projectile.Id);
			}
			AccessTools.Method(typeof(ProjectileManager), "AddToRemoveQueue").Invoke(__instance, new object[1] { projectile });
			_fuseTimes.Remove(projectile);
			HomingMissiles.OnProjectileGone(projectile);
			RocketHit(projectile, hit);
			return false;
		}

		[HarmonyPatch(typeof(ProjectileManager), "AddProjectile")]
		[HarmonyPostfix]
		public static void ProjectileManager_AddProjectile_Postfix(ProjectileManager __instance, Player owner, WeaponInfo weaponInfo, bool isLocal)
		{
			if (!Plugin.RocketTypeReady || weaponInfo == null || weaponInfo.ProjectileType != Plugin.RocketTypeId)
			{
				return;
			}
			// Der AddProjectile-Body haengt die Rakete an type.Projectiles - unsere ist die letzte.
			ProjectileType type = __instance.GetType(Plugin.RocketTypeId);
			if (type.Projectiles.Count == 0)
			{
				return;
			}
			Projectile newest = type.Projectiles[type.Projectiles.Count - 1];
			HomingMissiles.OnProjectileSpawned(newest, isLocal, owner);
		}

		[HarmonyPatch(typeof(ProjectileManager), "FixedUpdate")]
		[HarmonyPostfix]
		public static void ProjectileManager_FixedUpdate_Postfix(ProjectileManager __instance)
		{
			if (!Plugin.RocketTypeReady)
			{
				return;
			}
			HomingMissiles.SteerProjectiles(__instance);
			ApplyThrust(__instance);
			HandleFuse(__instance);
		}

		/// <summary>
		/// Vanilla entfernt Geschosse im Wasser oder ausserhalb der Reichweite kommentarlos.
		/// Eine Rakete muss dabei detonieren - auch wenn sie nie etwas getroffen hat.
		/// </summary>
		[HarmonyPatch(typeof(ProjectileManager), "UpdateProjectileScan")]
		[HarmonyPrefix]
		public static bool UpdateProjectileScan_Prefix(ProjectileManager __instance, Projectile projectile, ProjectileType type)
		{
			if (!Plugin.RocketTypeReady || projectile.TypeId != Plugin.RocketTypeId)
			{
				return true;
			}
			if (!projectile.Owner || (projectile.Position - projectile.Owner.Transform.position).sqrMagnitude > MaxRangeSqr(__instance))
			{
				Detonate(__instance, projectile, projectile.Position);
				return false;
			}
			// Wasser: erst ab etwas Abstand zum Abschusspunkt, sonst detoniert sie beim Schwimmen sofort im Gesicht.
			float waterHeight = WaterManager.GetWaterHeight(projectile.Position);
			if (projectile.Position.y < waterHeight && (projectile.Position - projectile.SpawnPos).sqrMagnitude > 4f)
			{
				Vector3 pos = projectile.Position;
				pos.y = waterHeight - 0.25f;
				Detonate(__instance, projectile, pos);
				return false;
			}
			return true;
		}

		/// <summary>
		/// Die Rakete wird von RocketVisuals als GameObject gezeichnet - der Instancing-Batch
		/// des Spiels bleibt fuer sie leer, sonst haetten wir sie doppelt.
		/// </summary>
		[HarmonyPatch(typeof(ProjectileManager), "UpdateMatrices")]
		[HarmonyPrefix]
		public static bool UpdateMatrices_Prefix(ProjectileType type)
		{
			return !Plugin.RocketTypeReady || type.MeshInstance != Plugin.RocketInstanceName;
		}

		private static float _maxRangeSqr = -1f;

		private static float MaxRangeSqr(ProjectileManager manager)
		{
			if (_maxRangeSqr < 0f)
			{
				_maxRangeSqr = (float)AccessTools.Field(typeof(ProjectileManager), "_sqrMaxProjRange").GetValue(manager);
				Plugin.Log.LogInfo($"Max. Geschossreichweite: {Mathf.Sqrt(_maxRangeSqr):0}m");
			}
			return _maxRangeSqr;
		}

		private static void Detonate(ProjectileManager manager, Projectile projectile, Vector3 pos)
		{
			_fuseTimes.Remove(projectile);
			HomingMissiles.OnProjectileGone(projectile);
			AccessTools.Method(typeof(ProjectileManager), "AddToRemoveQueue").Invoke(manager, new object[1] { projectile });
			ExplodeAt(pos, projectile.Owner);
		}

		/// <summary>Raketenmotor: die Rakete beschleunigt nach dem Abschuss bis zur Endgeschwindigkeit.</summary>
		private static void ApplyThrust(ProjectileManager manager)
		{
			float acceleration = (Plugin.CfgAcceleration != null) ? Plugin.CfgAcceleration.Value : 0f;
			if (acceleration <= 0f)
			{
				return;
			}
			ProjectileType type = manager.GetType(Plugin.RocketTypeId);
			foreach (Projectile projectile in type.Projectiles)
			{
				float speed = projectile.Velocity.magnitude;
				if (speed > 0.01f && speed < Plugin.MaxRocketSpeed)
				{
					float newSpeed = Mathf.Min(speed + acceleration * Time.fixedDeltaTime, Plugin.MaxRocketSpeed);
					projectile.Velocity *= newSpeed / speed;
				}
			}
		}

		/// <summary>Nach jedem Schuss auffuellen wenn: unendlich Ammo (AmmoPerMag=0 => Config 0)
		/// ODER Instant-Reload (ReloadSpeed 0/sehr klein). Sonst wuerde der Vanilla-Reload-Queue
		/// oder Ammo==0 die Waffe blockieren.
		/// Muss in dieser Klasse liegen - Start() patcht ausschliesslich typeof(Patches).</summary>
		[HarmonyPatch(typeof(Weapon), "Shoot")]
		[HarmonyPostfix]
		public static void Weapon_Shoot_Postfix(Weapon __instance)
		{
			if (Plugin.RocketLauncherItem == null)
			{
				return;
			}
			bool infiniteAmmo = Plugin.CfgAmmoPerMag != null && Plugin.CfgAmmoPerMag.Value <= 0;
			bool instantReload = Plugin.CfgReloadSpeed != null && Plugin.CfgReloadSpeed.Value <= 0.05f;
			if (!infiniteAmmo && !instantReload)
			{
				return;
			}
			Item item = __instance.GetComponent<Item>();
			if (item == null || item.ID != Plugin.ItemId)
			{
				return;
			}
			Attachments attachments = __instance.Attachments;
			if (attachments != null && __instance.Ammo != attachments.AmmoPerMag)
			{
				AccessTools.Property(typeof(Weapon), "Ammo").GetSetMethod(nonPublic: true).Invoke(__instance, new object[1] { attachments.AmmoPerMag });
				// Reload-Queue des Vanilla-Codes zuruecksetzen.
				SetField(__instance, "_queueReload", false);
				SetField(__instance, "_isReloading", false);
				SetField(__instance, "_reloadAmmoRefilled", true);
			}
		}

		/// <summary>Ueber die Item-ID pruefen, nicht ueber die Referenz auf das Template:
		/// RocketLauncherItem ist nur der Bauplan, jeder gespawnte Launcher ist ein eigenes
		/// Item. Mit dem Referenzvergleich hiess jeder echte Launcher in der Welt noch
		/// "Sturmgewehr".</summary>
		[HarmonyPatch(typeof(Item), "GetName")]
		[HarmonyPostfix]
		public static void Item_GetName_Postfix(Item __instance, ref string __result)
		{
			if (__instance != null && __instance.ID == Plugin.ItemId)
			{
				__result = "Rocket Launcher";
			}
		}

		private static readonly Dictionary<Projectile, float> _fuseTimes = new Dictionary<Projectile, float>();
		private static readonly List<Projectile> _fusedRockets = new List<Projectile>();

		private static float FuseSeconds => (Plugin.CfgFuseSeconds != null) ? Plugin.CfgFuseSeconds.Value : 4f;

		public static void RegisterFuse(Projectile projectile)
		{
			_fuseTimes[projectile] = FuseSeconds;
		}

		private static void HandleFuse(ProjectileManager manager)
		{
			ProjectileType type = manager.GetType(Plugin.RocketTypeId);
			foreach (Projectile projectile in type.Projectiles)
			{
				if (!_fuseTimes.ContainsKey(projectile))
				{
					_fuseTimes[projectile] = FuseSeconds;
				}
			}
			if (_fuseTimes.Count == 0)
			{
				return;
			}
			_fusedRockets.Clear();
			foreach (KeyValuePair<Projectile, float> item in _fuseTimes)
			{
				_fusedRockets.Add(item.Key);
			}
			foreach (Projectile item2 in _fusedRockets)
			{
				if (!type.Projectiles.Contains(item2))
				{
					_fuseTimes.Remove(item2);
					continue;
				}
				_fuseTimes[item2] -= Time.fixedDeltaTime;
				if (_fuseTimes[item2] <= 0f)
				{
					Detonate(manager, item2, item2.Position);
				}
			}
		}

		[HarmonyPatch(typeof(DazedCommands), "IsServerCommand")]
		[HarmonyPrefix]
		public static bool IsServerCommand_Prefix(string fullCommand, ref bool __result)
		{
			if (string.IsNullOrEmpty(fullCommand) || !fullCommand.StartsWith("/"))
			{
				return true;
			}
			string command = fullCommand.ToLower();
			if (command.StartsWith("/rocketbuy"))
			{
				HomingMissiles.Buy(Player.LocalPlayer);
				__result = true;
				return false;
			}
			if (command.StartsWith("/rocketcfg"))
			{
				ConfigCommand(fullCommand);
				__result = true;
				return false;
			}
			if (command.StartsWith("/rocketmodel"))
			{
				ModelCommand(fullCommand);
				__result = true;
				return false;
			}
			if (command.StartsWith("/rocket"))
			{
				SpawnRocketLauncher();
				__result = true;
				return false;
			}
			EnableCheats();
			return true;
		}

		public static void SpawnRocketLauncher()
		{
			try
			{
				if (Plugin.RocketLauncherItem == null)
				{
					ChatManager.ChatMessage("[RocketLauncherMod] weapon not ready yet, wait a moment");
					return;
				}
				if (!Server.Instance || !Server.Instance.IsServerInitialized)
				{
					// FishNet laesst nur den Server Objekte spawnen - das ist keine Mod-Grenze.
					ChatManager.ChatMessage("[RocketLauncherMod] only the host can spawn");
					return;
				}
				// Ohne registriertes Netzwerk-Prefab kommt beim Mitspieler ein Sturmgewehr an.
				if (!Plugin.EnsureNetworkPrefabRegistered())
				{
					ChatManager.ChatMessage("[RocketLauncherMod] Warnung: Netzwerk-Prefab nicht registriert - Mitspieler sehen ein Sturmgewehr (siehe BepInEx-Log)");
				}
				Vector3 position = GameInfo.CurCamera.transform.position + GameInfo.CurCamera.transform.forward * 2f;
				Item item = UnityEngine.Object.Instantiate(Plugin.RocketLauncherItem, position, Quaternion.identity);
				Server.Instance.Spawn(item.gameObject);
				ChatManager.ChatMessage("[RocketLauncherMod] Rocket Launcher spawned!");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("spawn failed: " + ex);
				ChatManager.ChatMessage("[RocketLauncherMod] spawn failed, see BepInEx log");
			}
		}

		/// <summary>
		/// /rocketcfg <setting> [wert] - Waffeneinstellungen live aendern. Ohne Wert: aktueller Stand.
		/// Wirkt auf das Template und alle bereits gespawnten Launcher (die kopieren die Felder).
		/// </summary>
		private static void ConfigCommand(string fullCommand)
		{
			string[] parts = fullCommand.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			string sub = (parts.Length > 1) ? parts[1].ToLower() : "show";
			try
			{
				switch (sub)
				{
				case "firerate":
					ApplyFloat(parts, Plugin.CfgTimeBetweenShots);
					break;
				case "ammo":
					ApplyInt(parts, Plugin.CfgAmmoPerMag);
					break;
				case "reload":
					ApplyFloat(parts, Plugin.CfgReloadSpeed, min: 0f);
					break;
				case "recoil":
					ApplyFloat(parts, Plugin.CfgScreenRecoil);
					break;
				case "modelrecoil":
					ApplyFloat(parts, Plugin.CfgModelRecoil);
					break;
				case "knockback":
					ApplyInt(parts, Plugin.CfgKnockback);
					break;
				case "reset":
					// Aus DefaultValue statt aus abgeschriebenen Literalen: die driften sonst
					// von den Config.Bind-Defaults weg, sobald dort einer geaendert wird.
					ResetToDefault(Plugin.CfgTimeBetweenShots);
					ResetToDefault(Plugin.CfgAmmoPerMag);
					ResetToDefault(Plugin.CfgReloadSpeed);
					ResetToDefault(Plugin.CfgScreenRecoil);
					ResetToDefault(Plugin.CfgModelRecoil);
					ResetToDefault(Plugin.CfgKnockback);
					// break statt return: sonst laeuft das ApplyToLaunchers() am Methodenende
					// nicht und der Reset blieb reine Kosmetik in der Config.
					break;
				case "show":
				case "help":
					ChatManager.ChatMessage("[Rocket] /rocketcfg firerate|ammo|reload|recoil|modelrecoil|knockback [wert] | reset - ohne Wert zeigt den aktuellen Stand");
					return;
				default:
					ChatManager.ChatMessage($"[Rocket] unbekannte Einstellung '{sub}'. Bekannt: firerate, ammo, reload, recoil, modelrecoil, knockback, reset");
					return;
				}
			}
			catch (Exception ex)
			{
				ChatManager.ChatMessage("[Rocket] Eingabe nicht lesbar: " + ex.Message);
				return;
			}
			ApplyToLaunchers();
			ChatManager.ChatMessage($"[Rocket] firerate {Plugin.CfgTimeBetweenShots.Value:0.###}s | ammo {Plugin.CfgAmmoPerMag.Value} | reload x{Plugin.CfgReloadSpeed.Value:0.##} | recoil {Plugin.CfgScreenRecoil.Value:0.##} | modelrecoil x{Plugin.CfgModelRecoil.Value:0.##} | knockback {Plugin.CfgKnockback.Value}");
		}

		/// <summary>Setzt einen Config-Eintrag auf den in Config.Bind hinterlegten Default.</summary>
		private static void ResetToDefault<T>(ConfigEntry<T> entry)
		{
			if (entry != null && entry.DefaultValue is T value)
			{
				entry.Value = value;
			}
		}

		private static void ApplyFloat(string[] parts, ConfigEntry<float> entry, float min = 0.01f)
		{
			if (parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
			{
				entry.Value = Mathf.Max(min, value);
				Plugin.Log.LogInfo($"{entry.Definition.Key} = {entry.Value}");
			}
		}

		private static void ApplyInt(string[] parts, ConfigEntry<int> entry)
		{
			if (parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
			{
				entry.Value = Mathf.Max(0, value);
				Plugin.Log.LogInfo($"{entry.Definition.Key} = {entry.Value}");
			}
		}

		/// <summary>Config-Staende auf Template + alle gespawnten Launcher-Exemplare anwenden.</summary>
		private static void ApplyToLaunchers()
		{
			ApplyToWeapon(Plugin.RocketLauncherItem != null ? Plugin.RocketLauncherItem.GetComponent<Weapon>() : null);
			foreach (Item item in UnityEngine.Object.FindObjectsOfType<Item>())
			{
				if (item.ID == Plugin.ItemId && (!Plugin.RocketLauncherItem || item != Plugin.RocketLauncherItem))
				{
					ApplyToWeapon(item.GetComponent<Weapon>());
				}
			}
		}

		private static void ApplyToWeapon(Weapon weapon)
		{
			if (weapon == null)
			{
				return;
			}
			SetField(weapon, "_timeBetweenShots", Plugin.CfgTimeBetweenShots.Value);
			Attachments attachments = weapon.GetComponent<Attachments>();
			Plugin.ApplyAmmo(attachments, weapon);
			Plugin.ApplyRecoil(weapon);
			Plugin.ApplyReloadSpeed(weapon);
		}

		private static void SetField(object obj, string field, object value)
		{
			AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
		}

		private static void ModelCommand(string fullCommand)
		{
			string[] parts = fullCommand.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			string sub = (parts.Length > 1) ? parts[1].ToLower() : "show";
			try
			{
				switch (sub)
				{
				case "pos":
					Plugin.CfgModelPos.Value = ParseVector(parts, Plugin.CfgModelPos.Value);
					break;
				case "nudge":
					Plugin.CfgModelPos.Value += ParseVector(parts, Vector3.zero);
					break;
				case "rot":
					Plugin.CfgModelRot.Value = ParseVector(parts, Plugin.CfgModelRot.Value);
					break;
				case "scale":
					if (parts.Length > 2)
					{
						Plugin.CfgModelScale.Value = float.Parse(parts[2], CultureInfo.InvariantCulture);
					}
					break;
				case "reset":
					// Vorher Nullvektor/Scale 1 - das ist NICHT der Default, sondern schob das
					// Modell aus der Hand. Die eingemessenen Werte stehen in Config.Bind.
					ResetToDefault(Plugin.CfgModelPos);
					ResetToDefault(Plugin.CfgModelRot);
					ResetToDefault(Plugin.CfgModelScale);
					break;
				case "export":
					ExportForBlender();
					return;
				case "help":
					ChatManager.ChatMessage("[Rocket] /rocketmodel pos X Y Z | nudge X Y Z | rot X Y Z | scale N | reset | export");
					return;
				}
			}
			catch (Exception ex)
			{
				ChatManager.ChatMessage("[Rocket] Eingabe nicht lesbar: " + ex.Message);
				return;
			}
			Plugin.ApplyModelTransform();
			Vector3 pos = Plugin.CfgModelPos.Value;
			Vector3 rot = Plugin.CfgModelRot.Value;
			ChatManager.ChatMessage($"[Rocket] pos {pos.x:0.###} {pos.y:0.###} {pos.z:0.###} | rot {rot.x:0.#} {rot.y:0.#} {rot.z:0.#} | scale {Plugin.CfgModelScale.Value:0.###}");
		}

		private static Vector3 ParseVector(string[] parts, Vector3 fallback)
		{
			if (parts.Length < 5)
			{
				return fallback;
			}
			return new Vector3(
				float.Parse(parts[2], CultureInfo.InvariantCulture),
				float.Parse(parts[3], CultureInfo.InvariantCulture),
				float.Parse(parts[4], CultureInfo.InvariantCulture));
		}

		/// <summary>
		/// Schreibt Haende, Original-Sturmgewehr und Launcher als eine OBJ im lokalen Raum der
		/// Waffe. In Blender liegt damit exakt die Situation vor, die man im Spiel sieht.
		/// </summary>
		private static void ExportForBlender()
		{
			if (Plugin.ModelTransform == null)
			{
				ChatManager.ChatMessage("[Rocket] Launcher noch nicht gebaut.");
				return;
			}
			Player player = Player.LocalPlayer;
			Item held = (bool)player ? player.Holding.HeldItem : null;
			Weapon weapon = (held != null) ? held.GetComponent<Weapon>() : null;
			if (weapon == null || held.ID != Plugin.ItemId)
			{
				ChatManager.ChatMessage("[Rocket] Bitte den Rocket Launcher in die Hand nehmen und nochmal /rocketmodel export.");
				return;
			}
			Transform root = weapon.transform;
			ObjWriter writer = new ObjWriter();

			Renderer hands = weapon.HandsMesh;
			if (hands is SkinnedMeshRenderer handsSkin && handsSkin.sharedMesh != null)
			{
				Mesh baked = new Mesh();
				handsSkin.BakeMesh(baked, useScale: true);
				writer.Add("Hands", baked, root, handsSkin.transform);
			}
			foreach (Renderer renderer in weapon.GetComponentsInChildren<Renderer>(includeInactive: true))
			{
				if (renderer == hands || renderer.gameObject.name != "AssaultRifle")
				{
					continue;
				}
				if (renderer is SkinnedMeshRenderer rifleSkin && rifleSkin.sharedMesh != null)
				{
					Mesh baked = new Mesh();
					rifleSkin.BakeMesh(baked, useScale: true);
					writer.Add("AssaultRifle_Referenz", baked, root, rifleSkin.transform);
				}
			}
			// Das Modell des gehaltenen Exemplars, nicht das des Templates.
			Transform model = root.Find(Plugin.ModelObjectName) ?? Plugin.ModelTransform;
			writer.Add("RocketLauncher", Plugin.LauncherMesh, root, model);

			string dir = Path.Combine(Application.streamingAssetsPath, "mods");
			string path = Path.Combine(dir, "blender_scene.obj");
			Directory.CreateDirectory(dir);
			File.WriteAllText(path, writer.Build());
			Vector3 pos = Plugin.CfgModelPos.Value;
			Plugin.Log.LogInfo("Blender-Export: " + path);
			Plugin.Log.LogInfo($"Aktuelle Werte: pos {pos.x:0.###} {pos.y:0.###} {pos.z:0.###}, scale {Plugin.CfgModelScale.Value:0.###}");
			ChatManager.ChatMessage("[Rocket] Export nach StreamingAssets/mods/blender_scene.obj");
		}

		/// <summary>Minimaler OBJ-Schreiber: alle Meshes in den lokalen Raum der Waffe umgerechnet.</summary>
		private class ObjWriter
		{
			private readonly StringBuilder _sb = new StringBuilder("# How to Fish - Rocket Launcher Ausrichtung\n# Koordinaten = lokaler Raum der Waffe (Unity: X rechts, Y hoch, Z vorne)\n");
			private int _offset = 1;

			public void Add(string name, Mesh mesh, Transform root, Transform source)
			{
				if (mesh == null)
				{
					return;
				}
				Vector3[] vertices = mesh.vertices;
				_sb.Append("o ").Append(name).Append('\n');
				foreach (Vector3 vertex in vertices)
				{
					Vector3 local = root.InverseTransformPoint(source.TransformPoint(vertex));
					_sb.AppendFormat(CultureInfo.InvariantCulture, "v {0:0.#####} {1:0.#####} {2:0.#####}\n", local.x, local.y, local.z);
				}
				for (int sub = 0; sub < mesh.subMeshCount; sub++)
				{
					int[] triangles = mesh.GetTriangles(sub);
					for (int i = 0; i + 2 < triangles.Length; i += 3)
					{
						_sb.AppendFormat("f {0} {1} {2}\n", triangles[i] + _offset, triangles[i + 1] + _offset, triangles[i + 2] + _offset);
					}
				}
				_offset += vertices.Length;
			}

			public string Build()
			{
				return _sb.ToString();
			}
		}

		public static void EnableCheats()
		{
			if (Plugin.CfgEnableCheats == null || !Plugin.CfgEnableCheats.Value)
			{
				return;
			}
			FieldInfo field = AccessTools.Field(typeof(ClientSettings), "<CheatsEnabled>k__BackingField");
			if (field != null)
			{
				field.SetValue(null, true);
			}
			FieldInfo field2 = AccessTools.Field(typeof(SteamManager), "<IsDev>k__BackingField");
			if (field2 != null)
			{
				field2.SetValue(null, true);
			}
		}

		private static void RocketHit(Projectile projectile, RaycastHit hit)
		{
			Vector3 pos = hit.point;
			Item directHitCreature = null;
			if (projectile.IsLocal)
			{
				Player playerFromBodyPart = PlayerManager.GetPlayerFromBodyPart(hit.transform);
				if (playerFromBodyPart != null)
				{
					playerFromBodyPart.Vitals.LocalHit(hit.point, projectile.Velocity.normalized, projectile.Owner, projectile.Damage, rangedHit: true, projectile.Velocity.normalized * GameInfo.PlayerKillForce, projectile.FromNpc);
				}
				Item item = ItemManager.Get(hit.collider);
				if (item != null)
				{
					// Merken, damit die Explosion diesen Fisch nicht ZUSATZLICH als Explosionskill zaehlt,
					// wenn der Direkttreffer ihn bereits getoetet hat (sonst doppelter Killscore-Eintrag).
					directHitCreature = item;
					item.LocalHit(hit.transform, hit.point, projectile.Velocity.normalized, projectile.Owner, projectile.Damage, rangedHit: true, projectile.Velocity.normalized * projectile.Force);
					if (item.Explosive != null)
					{
						item.Explosive.ForceExplode(projectile.Owner, instant: true);
					}
				}
				else if (hit.transform.CompareTag("NPC"))
				{
					DazedUtils.PlayDeadPlayerHitEffects(hit.point, projectile.Velocity, projectile.Damage, projectile.Owner, noDecals: true);
				}
			}
			ExplodeAt(pos, projectile.Owner, directHitCreature);
		}

		private static void ExplodeAt(Vector3 pos, Player player, Item directHitCreature = null)
		{
			ExplosionInfo info = Plugin.ExplosionInfo;
			if (info == null)
			{
				return;
			}
			bool underWater = WaterManager.GetWaterHeight(pos) > pos.y && info.HasUnderwaterExplosion;
			if (!ProjectileManager.Instance.IsServerInitialized)
			{
				ExplodeEffects(pos, info);
				return;
			}
			List<Creature> damaged = new List<Creature>();
			List<Creature> killed = new List<Creature>();
			List<Player> hitPlayers = new List<Player>();
			if (underWater)
			{
				SpawnDeadFish(pos, info, damaged, killed);
			}
			Collider[] array = Physics.OverlapSphere(pos, info.ForceRadius, GameInfo.AffectedByExplosionLayer);
			Collider[] array2 = array;
			foreach (Collider collider in array2)
			{
				Vector3 vector = pos - collider.transform.position;
				Player playerFromBodyPart = PlayerManager.GetPlayerFromBodyPart(collider.transform);
				if (playerFromBodyPart != null && !playerFromBodyPart.IsDeinitializing && vector.sqrMagnitude <= info.DamageRadius * info.DamageRadius)
				{
					// Unterwasser-Detonation verletzt Spieler nicht - genau wie Dynamit im Wasser.
					if (!underWater && !hitPlayers.Contains(playerFromBodyPart))
					{
						hitPlayers.Add(playerFromBodyPart);
						Vector3 force = vector.normalized * info.PlayerForce;
						Server.Instance.HitPlayer(playerFromBodyPart, info.Damage, force, collider.transform.position, 0, player);
						// Punkte fuer einen toedlichen Treffer auf Mitspieler - wie beim Dynamit.
						if ((bool)player && playerFromBodyPart != player && playerFromBodyPart.Vitals.Health > 0
							&& playerFromBodyPart.Vitals.Health - info.Damage <= 0 && ServerSettings.UseFriendlyFire)
						{
							SendExplosionKills(player, playerFromBodyPart.SteamName, 100);
						}
					}
					continue;
				}
				Item item = ItemManager.Get(collider);
				if (item == null)
				{
					// Boot-Kollider sind keine Items - ohne diesen Zweig spueren Explosionen
					// das Boot gar nicht (Vanilla-Dynamit schubst es via HiddenPhysicsRig weg).
					if (BoatManager.ColToBoat.TryGetValue(collider, out Boat boat))
					{
						boat.HiddenPhysicsRig.AddExplosionForce(info.BoatForce, pos, info.DamageRadius, 2f);
					}
					continue;
				}
				if (item.Creature != null && !damaged.Contains(item.Creature) && !item.Creature.IsDead && vector.sqrMagnitude <= info.DamageRadius * info.DamageRadius)
				{
					// Direkttreffer hat den Fisch lokal schon als tot verbucht (Killscore lief dort ueber
					// GetRangedBonuses) -> kein zweiter Eintrag als Explosionskill. Lebt er noch (z.B. Wal,
					// der 150 Direktschaden ueberlebt), nimmt er ganz normal Schaden durch die Explosion.
					bool killedByDirectHit = item == directHitCreature
						&& _localIsDeadField != null && (bool)_localIsDeadField.GetValue(item.Creature);
					if (!killedByDirectHit)
					{
						int damage = DamageOnCreature(info.Damage, item.Creature);
						item.Creature.ServerChangeHp(damage);
						// Reihenfolge wie im Original: Hp ist hier noch der Wert von vor dem Treffer.
						if (item.Creature.Hp - damage <= 0)
						{
							killed.Add(item.Creature);
							if ((bool)player && (bool)item.Creature.Bird)
							{
								SendSeagullAchievement(player);
							}
						}
						damaged.Add(item.Creature);
					}
				}
				item.RigidbodySync.StartSimulateLocal();
				Vector3 vector2 = item.transform.position - pos;
				vector2.y += 2f;
				vector2.Normalize();
				vector2 *= info.ItemForce;
				item.Rig.AddForce(vector2);
				item.WasInteractedWith();
				if (item.Explosive != null)
				{
					item.Explosive.ForceExplode(player, instant: false);
				}
			}
			foreach (Creature creature in damaged)
			{
				creature.ObserverExplosionHit(player, creature.transform.position - pos, DamageOnCreature(info.Damage, creature));
			}
			AwardKillScore(player, killed);
			ExplodeEffects(pos, info);
		}

		/// <summary>
		/// Killscore fuer Explosionskills - benutzt die TargetRpc des Spiels, damit die Punkte
		/// beim Schuetzen ankommen (auch im Multiplayer) und dieselbe Bonus-Berechnung
		/// (KillScoreCalculator.GetExplosionBonuses) durchlaufen wie beim Dynamit.
		/// </summary>
		private static void AwardKillScore(Player player, List<Creature> killed)
		{
			if (killed.Count == 0)
			{
				return;
			}
			Dictionary<string, Vector2Int> summary = new Dictionary<string, Vector2Int>();
			foreach (Creature creature in killed)
			{
				string name = creature.GetName();
				// TotalWorth bewusst vor SetKillscoreMultiplier lesen - wie im Original.
				if (!summary.TryGetValue(name, out Vector2Int entry))
				{
					summary[name] = new Vector2Int(1, creature.TotalWorth);
				}
				else
				{
					summary[name] = entry + new Vector2Int(1, creature.TotalWorth);
				}
			}
			string names = "";
			int worth = 0;
			foreach (KeyValuePair<string, Vector2Int> entry in summary)
			{
				if (names != "")
				{
					names += ", ";
				}
				names += (entry.Value.x != 1) ? $"{entry.Value.x}x {entry.Key}" : entry.Key;
				worth += entry.Value.y;
			}
			if ((bool)player)
			{
				SendExplosionKills(player, names, worth);
			}
			foreach (Creature creature in killed)
			{
				creature.SetKillscoreMultiplier(1.25f);
			}
			Plugin.Log.LogInfo($"Explosionskill: {names} (+{worth})");
		}

		private static readonly MethodInfo _sendExplosionKills = AccessTools.Method(typeof(ExplosionManager), "SendExplosionKills");
		private static readonly MethodInfo _sendSeagullAchievement = AccessTools.Method(typeof(ExplosionManager), "SendSeagullDynamiteKillAchievement");
		private static readonly FieldInfo _localIsDeadField = AccessTools.Field(typeof(Creature), "_localIsDead");

		private static void SendExplosionKills(Player player, string names, int worth)
		{
			if (ExplosionManager.Instance == null || _sendExplosionKills == null || player.Owner == null)
			{
				return;
			}
			_sendExplosionKills.Invoke(ExplosionManager.Instance, new object[3] { player.Owner, names, worth });
		}

		private static void SendSeagullAchievement(Player player)
		{
			if (ExplosionManager.Instance == null || _sendSeagullAchievement == null || player.Owner == null)
			{
				return;
			}
			_sendSeagullAchievement.Invoke(ExplosionManager.Instance, new object[1] { player.Owner });
		}

		private static int DamageOnCreature(int baseDamage, Creature creature)
		{
			return baseDamage + (int)((float)creature.MaxHp * 0.05f);
		}

		/// <summary>
		/// Dynamit-im-Wasser-Effekt: die Detonation treibt tote Fische an die Oberflaeche.
		/// Die zaehlen wie beim Dynamit als Kills und wandern deshalb in die Killscore-Liste.
		/// </summary>
		private static void SpawnDeadFish(Vector3 pos, ExplosionInfo info, List<Creature> damaged, List<Creature> killed)
		{
			ExplosionManager explosionManager = ExplosionManager.Instance;
			if (explosionManager == null || CreatureManager.Instance == null)
			{
				return;
			}
			List<ItemInfoWeight> weights = AccessTools.Field(typeof(ExplosionManager), "_explodabaleItemWeights").GetValue(explosionManager) as List<ItemInfoWeight>;
			if (weights == null || weights.Count == 0)
			{
				return;
			}
			Vector3 surface = pos;
			surface.y = WaterManager.GetWaterHeight(pos) - 0.1f;
			int count = UnityEngine.Random.Range(info.UnderWaterFishMinMax.x, info.UnderWaterFishMinMax.y);
			for (int i = 0; i < count; i++)
			{
				Fishable fishable = CreatureManager.Instance.GetRandomItem(surface, weights);
				if (fishable == null || fishable.ItemToSpawn == null)
				{
					continue;
				}
				Vector3 offset = UnityEngine.Random.insideUnitSphere * info.ForceRadius / 2f;
				offset.y = 0f;
				Vector3 spawnPos = surface + offset;
				Item fish = UnityEngine.Object.Instantiate(fishable.ItemToSpawn, spawnPos, Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360), 0f));
				if (fish.Creature != null)
				{
					fish.Creature.ServerKillOnSpawn();
					damaged.Add(fish.Creature);
					killed.Add(fish.Creature);
				}
				explosionManager.Spawn(fish.gameObject);
				Vector3 force = (spawnPos - surface).normalized * (info.ItemForce * 0.15f);
				force.y = Mathf.Abs(info.ItemForce) * UnityEngine.Random.Range(0.8f, 1.2f);
				fish.RigidbodySync.StartSimulateLocal();
				fish.Rig.AddForce(force);
			}
		}

		private static void ExplodeEffects(Vector3 pos, ExplosionInfo info)
		{
			float waterHeight = WaterManager.GetWaterHeight(pos);
			if (waterHeight > pos.y && info.HasUnderwaterExplosion)
			{
				VFXManager.Play("WaterExplosion", new Vector3(pos.x, waterHeight + 0.15f, pos.z), Vector3.zero);
				AudioManager.PlayRandomClipAt("ExplosionUnderwater_V", 1, 3, pos, variation: false, AudioDistance.Long, info.ExplosionSoundVol, 0.2f);
			}
			else
			{
				ParticleManager.Play(info.ExplosionParticleName, pos, Vector3.zero);
				if (Physics.Raycast(pos, Vector3.down, 1f, GameInfo.LevelLayer))
				{
					ParticleManager.Play("Ashes", pos, Vector3.up);
				}
				AudioManager.PlayClipAt(info.ExplosionSoundName, pos, variation: true, AudioDistance.Long, info.ExplosionSoundVol, 0.2f);
			}
			if ((bool)Player.LocalPlayer)
			{
				Player.LocalPlayer.ScreenShake.ShakeAt(pos, 1, info.ScreenShakeAmount);
			}
		}
	}
}