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
				"Enables the game's built-in dev cheats (/spawn, /money ...). WARNING: this also unlocks the cheat hotkeys - T = slow motion (1x/0.1x/0.01x), G = damage, H = heal, M/N = money, O = island, U = UI. The Rocket Launcher does NOT need this (/rocket always works).");
			CfgKeepTimeScaleNormal = Config.Bind("General", "KeepTimeScaleNormal", true,
				"Automatically resets Time.timeScale to 1 if something switches the game into slow motion.");
			// Absolute values instead of factors: rifle bullets fly at 900 m/s in this game.
			// "A bit slower" would still be invisible - a visible rocket needs 40-90 m/s.
			CfgLaunchSpeed = Config.Bind("Rocket", "LaunchSpeed", 40f,
				"Launch speed of the rocket in m/s when it leaves the tube. (Assault rifle bullet for comparison: 900 m/s)");
			CfgMaxSpeed = Config.Bind("Rocket", "MaxSpeed", 80f,
				"Top speed in m/s that the rocket motor accelerates to.");
			CfgAcceleration = Config.Bind("Rocket", "Acceleration", 30f,
				"Thrust of the rocket motor in m/s^2 up to MaxSpeed. 0 = constant speed.");
			CfgGravity = Config.Bind("Rocket", "Gravity", 1.5f,
				"Gravity applied to the rocket in m/s^2. 0 = perfectly straight, 1.5 = slight drop over distance.");
			CfgFuseSeconds = Config.Bind("Rocket", "FuseSeconds", 4f,
				"After this flight time, the rocket detonates even without a hit. Note: projectiles are forcibly removed 400 m from the shooter - LaunchSpeed x FuseSeconds should stay below that.");
			CfgTimeBetweenShots = Config.Bind("Rocket", "TimeBetweenShots", 1.2f,
				"Fire rate: seconds between two shots.");
			CfgMeshScale = Config.Bind("Rocket", "MeshScale", 2f,
				"Size of the flying rocket. 1 = rocket.obj at its original size (about 30 cm long).");
			CfgAmmoPerMag = Config.Bind("Rocket", "AmmoPerMag", 1,
				"Shots per magazine. 0 = infinite ammo (no reload needed).");

			// Recoil: ScreenRecoil = camera kick (degrees upward), ModelRecoil = weapon model travel.
			// Defaults are well above assault-rifle level - a recoil tube should punch back noticeably.
			CfgScreenRecoil = Config.Bind("Recoil", "ScreenRecoil", 9f,
				"Camera recoil per shot (degrees). Assault rifle is around 1.5.");
			CfgModelRecoil = Config.Bind("Recoil", "ModelRecoil", 4f,
				"Factor for the weapon model's recoil (ToolMovement.Recoil).");
			CfgKnockback = Config.Bind("Recoil", "Knockback", 25,
				"Player knockback backwards per shot (m/s applied to the Rigidbody). Assault rifle: ~2.");
			CfgReloadSpeed = Config.Bind("Rocket", "ReloadSpeed", 2f,
				"Speed of the reload animation (2 = double speed, i.e. half the reload time).");

			// Manually adjusted in-game until the launcher tip sat in the front hand
			// (calibrated against the rest-pose matrix in CalculateAnchor/Configure).
			CfgModelPos = Config.Bind("Model", "Position", new Vector3(306.154f, 9.013f, 10.704f),
				"Offset of the launcher model in the hand (x=right, y=up, z=forward), in meters. Changeable live with /rocketmodel pos X Y Z.");
			CfgModelRot = Config.Bind("Model", "Rotation", new Vector3(0f, 0f, 0f),
				"Additional rotation of the launcher model in degrees. Changeable live with /rocketmodel rot X Y Z.");
			CfgModelScale = Config.Bind("Model", "Scale", 1.5f,
				"Size of the launcher model in the hand. Changeable live with /rocketmodel scale N.");

			HomingMissiles.Bind(Config);
			ModCredits.Bind(Config);
		}

		private void Update()
		{
			if (CfgKeepTimeScaleNormal != null && CfgKeepTimeScaleNormal.Value && Time.timeScale != 1f)
			{
				Log.LogWarning($"Time.timeScale was {Time.timeScale} - resetting to 1 (slow-motion cheat?)");
				Time.timeScale = 1f;
			}
			EnsureInstanceRegistered();
			EnsureNetworkPrefabRegistered();
			UpdateHomingLock();
		}

		private static void UpdateHomingLock()
		{
			// Reset on scene change/game end: no LocalPlayer left means the session has ended.
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
		/// InstanceManager.Awake() rebuilds its dictionary from _instanceTypes on every scene change.
		/// Without re-registering, the rocket mesh type would be gone afterwards (and ReplaceBatches would throw).
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
			Log.LogInfo("Rocket mesh re-registered (scene change)");
			return true;
		}

		private static InstanceManager _instanceManager;

		private static InstanceManager GetInstanceManager()
		{
			// The old object is destroyed on scene change - Unity then reports it as == null.
			if (_instanceManager == null)
			{
				_instanceManager = UnityEngine.Object.FindObjectOfType<InstanceManager>();
			}
			return _instanceManager;
		}

		private static readonly FieldInfo _instanceTypeDicField = AccessTools.Field(typeof(InstanceManager), "_instanceTypeDic");

		/// <summary>Entry from the private static _instanceTypeDic, or null.</summary>
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
				Log.LogError("Harmony patches failed: " + ex);
				yield break;
			}
			try
			{
				// Cosmetic, so it gets its own pass: a throw in here must not take
				// the launcher down with it (the catch above aborts the mod).
				new Harmony("com.kimox.rocketlauncher.credits").PatchAll(typeof(ModCredits.CreditsPatches));
			}
			catch (Exception ex)
			{
				Log.LogWarning("Credits patches failed - mod credits will be missing: " + ex);
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
			// rotateX=0: the long axis of the OBJ sits on +Z - exactly the axis that
			// MatrixFromProjectile rotates into the flight direction via LookRotation.
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
			// The instance type stays registered only as a placeholder so vanilla code doesn't reach into nothing.
			// The rocket is actually drawn by RocketVisuals as a real GameObject - GPU instancing with the
			// game's projectile material didn't make anything visible (bullets fly at 900 m/s, so it doesn't stand out there).
			AddInstanceType(instanceManager, _rocketMesh, _rocketMaterial);
			RocketVisuals.Setup(_rocketMesh, _rocketMaterial);
			Log.LogInfo($"Rocket mesh loaded (verts: {_rocketMesh.vertexCount}, bounds: {_rocketMesh.bounds.size}, shader: {_rocketMaterial.shader.name})");
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
		/// Dedicated FishNet prefab collection for mod items. Must be != 0 (0 is the vanilla
		/// list baked into the NetworkManager) and identical on host and client.
		/// A dedicated collection is preferable to the vanilla list: there, the PrefabId
		/// would be the current list index and thus depend on both sides having loaded
		/// exactly the same number of prefabs. Here it's always 0.
		/// </summary>
		public const ushort NetworkCollectionId = 20200;

		private static NetworkManager _registeredWith;

		/// <summary>
		/// Without this, the launcher is not multiplayer-capable: the template is a
		/// runtime clone of the assault rifle prefab and carries over its serialized
		/// NetworkObject.PrefabId. FishNet only writes this ID into the packet on spawn
		/// (ManagedObjects.WriteSpawn), and the client looks it up in its own prefab list
		/// (ClientObjects.GetInstantiatedNetworkObject) - so it instantiates the VANILLA
		/// assault rifle. ID 200, model, weapon stats and ProjectileType only live on the
		/// clone belonging to the machine that built it.
		///
		/// AddObject sets PrefabId and SpawnableCollectionId on the template via
		/// InitializePrefabRange; both are transmitted in the spawn packet
		/// (WriteSpawnedNetworkObject writes the CollectionId). A client with the mod has
		/// the same collection registered and thus instantiates its own launcher template.
		///
		/// Idempotent: checkForDuplicates prevents duplicate entries, so this can run from
		/// Update() against a NetworkManager change on scene switch.
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
			// Registration remains valid as long as the same NetworkManager still owns the collection.
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
				Log.LogError($"FishNet collection {NetworkCollectionId} could not be created - launcher stays singleplayer-only.");
				return false;
			}
			// Runtime ScriptableObject: without this flag, Resources.UnloadUnusedAssets can
			// clean up the collection on scene change even though the NetworkManager still
			// references it in _runtimeSpawnablePrefabs.
			prefabs.hideFlags |= HideFlags.DontUnloadUnusedAsset;
			prefabs.AddObject(nob, checkForDuplicates: true);
			_registeredWith = manager;
			Log.LogInfo($"Network prefab registered: CollectionId {nob.SpawnableCollectionId}, PrefabId {nob.PrefabId} (previously the assault rifle's PrefabId - which is why a fellow player saw an assault rifle)");
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
			// Enable ADS - needed for the homing lock (lock box appears while aiming).
			SetField(weapon, "_canAds", true);
			float bulletSpeed = (float)AccessTools.Field(typeof(Weapon), "_projSpeed").GetValue(weapon);
			float launchSpeed = Mathf.Max(1f, CfgLaunchSpeed.Value);
			MaxRocketSpeed = Mathf.Max(launchSpeed, CfgMaxSpeed.Value);
			SetField(weapon, "_projSpeed", launchSpeed);
			Log.LogInfo($"Rocket: start {launchSpeed:0.#} m/s -> max {MaxRocketSpeed:0.#} m/s (assault rifle bullet: {bulletSpeed:0.#} m/s), thrust {CfgAcceleration.Value}, gravity {CfgGravity.Value}, fuse {CfgFuseSeconds.Value}s, range approx. {Mathf.Min(launchSpeed * CfgFuseSeconds.Value * 1.6f, 400f):0}m");
			Attachments component = weapon.GetComponent<Attachments>();
			ApplyAmmo(component, weapon);
			ApplyRecoil(weapon);
		}

		/// <summary>Apply AmmoPerMag. 0 = infinite: internally a large magazine (999) is
		/// set, because AmmoPerMag=0 itself would be a dead end (Ammo=0 => Shoot aborts,
		/// Reload refills to 0). Combined with the instant-reload postfix, this results in continuous fire.</summary>
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
		/// Recoil: ScreenRecoil/ModelRecoil scale the assault rifle's BarrelAttachment
		/// values. Default 1.0 is the vanilla rifle - higher = harder kick.
		/// Applied to ALL barrels of the weapon, because Attachments accesses them via the
		/// SyncVar index _syncedBarrelAttachment - which barrel is active can change.
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
			// _barrelAttachments is a List<BarrelAttachment>, not a single attachment.
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
		/// Capture the vanilla values exactly once - on the very first call, which always
		/// runs on the freshly cloned template (ConfigureWeapon in BuildWeapon). Per barrel
		/// INDEX, not per instance: every spawned launcher clones the template's already-
		/// multiplied values. If we took those as the starting value, recoil would compound
		/// with every /rocketcfg recoil.
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
			Log.LogInfo($"Vanilla recoil captured ({barrels.Count} barrel(s)): screen {_vanillaScreenRecoil[0]}, modelMulti {_vanillaWeaponRecoilMulti[0]}");
		}

		private static Vector2[] _vanillaScreenRecoil;
		private static float[] _vanillaWeaponRecoilMulti;

		/// <summary>Set the reload animation speed (legacy animation, via AnimationState.speed).
		/// ReloadSpeed 0 = instant reload: after every shot, the magazine is immediately full again.</summary>
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
			// rotateX=0: the long axis of the OBJ stays on +Z - the weapon's forward direction in Unity.
			// With the old 270 degrees, the 65 cm long launcher stood vertically in front of the camera.
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
				// Largest real mesh renderer = the assault rifle itself: our position reference.
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
			// The model stays a child of the weapon root: this keeps it visible in first-person view
			// and also present on the dropped item (the rifle rig hierarchy gets deactivated
			// on drop - a model underneath it would disappear).
			modelObject.transform.SetParent(weapon.transform, worldPositionStays: false);
			modelObject.AddComponent<MeshFilter>().sharedMesh = LauncherMesh;
			modelObject.AddComponent<MeshRenderer>().sharedMaterial = material;
			ModelTransform = modelObject.transform;
			// LauncherGlue glues the model to Item._handModelRight - the right hand's animated
			// IK target (PlayerHands.SyncHiddenHandsToAnimatedTool sets the hand bones
			// exactly there). This transform is a normal child of the item, is moved by the
			// entire movement chain (sway/bob/recoil/ADS + firing/reload rig), and
			// has no SkinnedMesh bindpose scaling. Bones, renderer transform and
			// sway transform all proved unusable as anchors.
			modelObject.AddComponent<LauncherGlue>();
			Transform handAnchor = (Transform)AccessTools.Field(typeof(Item), "_handModelRight").GetValue(weapon.GetComponent<Item>());
			CalculateAnchor(weapon.transform, handAnchor);
			ApplyModelTransform();
			Log.LogInfo($"Weapon model swapped: launcher mesh ({LauncherMesh.vertexCount} verts, bounds {LauncherMesh.bounds.size}), reference renderer: {(ReferenceRenderer != null ? ReferenceRenderer.gameObject.name : "none")}");
		}

		public const string ModelObjectName = "ModModel";

		public static Transform ModelTransform;
		public static Mesh LauncherMesh;
		public static Renderer ReferenceRenderer;
		public static Bounds ReferenceBounds;

		/// <summary>Position of the original rifle's center in the weapon's local space.</summary>
		private static Vector3 _anchorLocal;

		/// <summary>Mapping from local weapon-root space to hand-anchor space, measured in the
		/// rest pose while building the template. Applies to all instances, because spawned
		/// copies share the same starting hierarchy.</summary>
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
		/// Places the launcher model where the assault rifle used to sit, plus fine-tuning
		/// from the config. Affects ALL instances - the template and every spawned launcher,
		/// because the model is copied along on spawn and then lives on independently.
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
				Log.LogInfo($"Launcher model updated ({count} instance(s)): pos {position}, scale {scale}");
			}
		}

		/// <summary>Model pose from the config, in the local space of the weapon root.</summary>
		private static void GetModelPose(out Vector3 position, out Quaternion rotation, out float scale)
		{
			scale = Mathf.Max(0.01f, CfgModelScale.Value);
			rotation = Quaternion.Euler(CfgModelRot.Value);
			position = _anchorLocal - rotation * (LauncherMesh.bounds.center * scale) + CfgModelPos.Value;
		}

		/// <summary>
		/// Glues the launcher model to this instance's Item._handModelRight - the right
		/// hand's animated IK target (PlayerHands.SyncHiddenHandsToAnimatedTool sets the
		/// hand bones exactly there every LateUpdate, Tool.TryActivateAnimatedHands uses it
		/// as the IK target). This makes the launcher follow the entire movement chain:
		/// sway/bob/recoil/ADS via the tool hierarchy plus the rig's firing/reload
		/// animations. The model itself stays a child of the weapon root (so it stays
		/// visible when dropped) and is only set to the anchor pose plus offset every LateUpdate.
		/// </summary>
		private class LauncherGlue : MonoBehaviour
		{
			private Transform _anchor;

			private Vector3 _anchorLocalPos;

			private Quaternion _anchorLocalRot;

			private Vector3 _localScale = Vector3.one;

			/// <summary>True once Configure has run once.</summary>
			public bool IsConfigured { get; private set; }

			/// <summary>
			/// A cloned instance - via /rocket on the host, via the FishNet spawn on a
			/// fellow player - does carry this component, but not its private fields:
			/// Unity only copies serialized state on Instantiate. It therefore pulls its own
			/// pose here. Previously, Update() searched the entire scene for this every
			/// frame via Resources.FindObjectsOfTypeAll.
			/// </summary>
			private void Awake()
			{
				if (!IsConfigured && LauncherMesh != null)
				{
					GetModelPose(out Vector3 position, out Quaternion rotation, out float scale);
					Configure(position, rotation, scale, _rootToAnchorRest);
				}
			}

			/// <summary>Offset in the local space of the weapon root (this is how the values arrive
			/// from /rocketmodel); converted here into this instance's anchor space against the rest pose.</summary>
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

			/// <summary>Find this instance's hand anchor: walk up the hierarchy from the model
			/// to the Weapon component, then pull the private _handModelRight from there. The
			/// template is inactive - its LateUpdate never runs, so the search only starts on
			/// active instances.</summary>
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
			// InstanceManager.RenderBatches() draws every registered instance type via
			// DrawMeshInstanced - without this flag, that throws an exception every frame.
			material.enableInstancing = true;
			return material;
		}

		private static void SetField(object obj, string field, object value)
		{
			AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
		}
	}

	/// <summary>
	/// Draws each flying rocket as its own GameObject. The game renders projectiles via
	/// Graphics.DrawMeshInstanced - for a 45 cm long rocket, a normal MeshRenderer is
	/// more reliable (and later allows a smoke trail/light as child objects).
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
			// Same interpolation as the game: the positions come from FixedUpdate.
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
			// No count comparison as a shortcut: if a rocket detonates in the same frame that
			// a new one launches, the counts match - the dead rocket's entry, GameObject and
			// all, would stick around forever. With a handful of rockets, the full scan is
			// essentially free anyway.
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
			// The AddProjectile body appends the rocket to type.Projectiles - ours is the last one.
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
		/// Vanilla removes projectiles in water or out of range silently.
		/// A rocket must detonate in that case - even if it never hit anything.
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
			// Water: only once there's some distance from the launch point, otherwise it detonates in your face immediately while swimming.
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
		/// The rocket is drawn by RocketVisuals as a GameObject - the game's instancing
		/// batch stays empty for it, otherwise we'd render it twice.
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
				Plugin.Log.LogInfo($"Max. projectile range: {Mathf.Sqrt(_maxRangeSqr):0}m");
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

		/// <summary>Rocket motor: the rocket accelerates after launch up to its top speed.</summary>
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

		/// <summary>Refill after every shot when: infinite ammo (AmmoPerMag=0 => config 0)
		/// OR instant reload (ReloadSpeed 0/very small). Otherwise the vanilla reload queue
		/// or Ammo==0 would block the weapon.
		/// Must live in this class - Start() only patches typeof(Patches).</summary>
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
				// Reset the vanilla code's reload queue.
				SetField(__instance, "_queueReload", false);
				SetField(__instance, "_isReloading", false);
				SetField(__instance, "_reloadAmmoRefilled", true);
			}
		}

		/// <summary>Check via the item ID, not via a reference to the template:
		/// RocketLauncherItem is only the blueprint, every spawned launcher is its own
		/// item. With a reference comparison, every real launcher in the world was still
		/// called "Assault Rifle".</summary>
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
					// FishNet only lets the server spawn objects - that's not a mod limitation.
					ChatManager.ChatMessage("[RocketLauncherMod] only the host can spawn");
					return;
				}
				// Without a registered network prefab, a fellow player sees an assault rifle.
				if (!Plugin.EnsureNetworkPrefabRegistered())
				{
					ChatManager.ChatMessage("[RocketLauncherMod] Warning: network prefab not registered - fellow players will see an assault rifle (see BepInEx log)");
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
		/// /rocketcfg <setting> [value] - change weapon settings live. Without a value: shows the current state.
		/// Affects the template and all already spawned launchers (they copy the fields).
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
					// From DefaultValue rather than copied-out literals: otherwise these would
					// drift from the Config.Bind defaults as soon as one of them is changed.
					ResetToDefault(Plugin.CfgTimeBetweenShots);
					ResetToDefault(Plugin.CfgAmmoPerMag);
					ResetToDefault(Plugin.CfgReloadSpeed);
					ResetToDefault(Plugin.CfgScreenRecoil);
					ResetToDefault(Plugin.CfgModelRecoil);
					ResetToDefault(Plugin.CfgKnockback);
					// break instead of return: otherwise ApplyToLaunchers() at the end of the
					// method wouldn't run and the reset would stay purely cosmetic in the config.
					break;
				case "show":
				case "help":
					ChatManager.ChatMessage("[Rocket] /rocketcfg firerate|ammo|reload|recoil|modelrecoil|knockback [value] | reset - without a value shows the current state");
					return;
				default:
					ChatManager.ChatMessage($"[Rocket] unknown setting '{sub}'. Known: firerate, ammo, reload, recoil, modelrecoil, knockback, reset");
					return;
				}
			}
			catch (Exception ex)
			{
				ChatManager.ChatMessage("[Rocket] input not readable: " + ex.Message);
				return;
			}
			ApplyToLaunchers();
			ChatManager.ChatMessage($"[Rocket] firerate {Plugin.CfgTimeBetweenShots.Value:0.###}s | ammo {Plugin.CfgAmmoPerMag.Value} | reload x{Plugin.CfgReloadSpeed.Value:0.##} | recoil {Plugin.CfgScreenRecoil.Value:0.##} | modelrecoil x{Plugin.CfgModelRecoil.Value:0.##} | knockback {Plugin.CfgKnockback.Value}");
		}

		/// <summary>Resets a config entry to the default stored in Config.Bind.</summary>
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

		/// <summary>Apply config values to the template + all spawned launcher instances.</summary>
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
					// Previously a zero vector/scale 1 - that is NOT the default, it pushed the
					// model out of the hand. The measured-in values live in Config.Bind.
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
				ChatManager.ChatMessage("[Rocket] input not readable: " + ex.Message);
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
		/// Writes hands, original assault rifle, and launcher as one OBJ in the weapon's
		/// local space. In Blender this gives you exactly the situation you see in-game.
		/// </summary>
		private static void ExportForBlender()
		{
			if (Plugin.ModelTransform == null)
			{
				ChatManager.ChatMessage("[Rocket] Launcher not built yet.");
				return;
			}
			Player player = Player.LocalPlayer;
			Item held = (bool)player ? player.Holding.HeldItem : null;
			Weapon weapon = (held != null) ? held.GetComponent<Weapon>() : null;
			if (weapon == null || held.ID != Plugin.ItemId)
			{
				ChatManager.ChatMessage("[Rocket] Please hold the Rocket Launcher and run /rocketmodel export again.");
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
					writer.Add("AssaultRifle_Reference", baked, root, rifleSkin.transform);
				}
			}
			// The model of the held instance, not the template's.
			Transform model = root.Find(Plugin.ModelObjectName) ?? Plugin.ModelTransform;
			writer.Add("RocketLauncher", Plugin.LauncherMesh, root, model);

			string dir = Path.Combine(Application.streamingAssetsPath, "mods");
			string path = Path.Combine(dir, "blender_scene.obj");
			Directory.CreateDirectory(dir);
			File.WriteAllText(path, writer.Build());
			Vector3 pos = Plugin.CfgModelPos.Value;
			Plugin.Log.LogInfo("Blender export: " + path);
			Plugin.Log.LogInfo($"Current values: pos {pos.x:0.###} {pos.y:0.###} {pos.z:0.###}, scale {Plugin.CfgModelScale.Value:0.###}");
			ChatManager.ChatMessage("[Rocket] Exported to StreamingAssets/mods/blender_scene.obj");
		}

		/// <summary>Minimal OBJ writer: all meshes converted into the weapon's local space.</summary>
		private class ObjWriter
		{
			private readonly StringBuilder _sb = new StringBuilder("# How to Fish - Rocket Launcher Alignment\n# Coordinates = local space of the weapon (Unity: X right, Y up, Z forward)\n");
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
					// Remember this so the explosion doesn't ADDITIONALLY count this fish as an explosion
					// kill if the direct hit already killed it (otherwise a duplicate killscore entry).
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
					// Underwater detonation doesn't injure players - just like dynamite in water.
					if (!underWater && !hitPlayers.Contains(playerFromBodyPart))
					{
						hitPlayers.Add(playerFromBodyPart);
						Vector3 force = vector.normalized * info.PlayerForce;
						Server.Instance.HitPlayer(playerFromBodyPart, info.Damage, force, collider.transform.position, 0, player);
						// Points for a lethal hit on a fellow player - same as with dynamite.
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
					// Boat colliders are not items - without this branch, explosions wouldn't
					// affect the boat at all (vanilla dynamite pushes it away via HiddenPhysicsRig).
					if (BoatManager.ColToBoat.TryGetValue(collider, out Boat boat))
					{
						boat.HiddenPhysicsRig.AddExplosionForce(info.BoatForce, pos, info.DamageRadius, 2f);
					}
					continue;
				}
				if (item.Creature != null && !damaged.Contains(item.Creature) && !item.Creature.IsDead && vector.sqrMagnitude <= info.DamageRadius * info.DamageRadius)
				{
					// A direct hit has already booked the fish as dead locally (killscore went through
					// GetRangedBonuses there) -> no second entry as an explosion kill. If it's still
					// alive (e.g. a whale that survives 150 direct damage), it takes normal explosion damage.
					bool killedByDirectHit = item == directHitCreature
						&& _localIsDeadField != null && (bool)_localIsDeadField.GetValue(item.Creature);
					if (!killedByDirectHit)
					{
						int damage = DamageOnCreature(info.Damage, item.Creature);
						item.Creature.ServerChangeHp(damage);
						// Order matches the original: Hp here is still the value from before the hit.
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
		/// Killscore for explosion kills - uses the game's TargetRpc so the points reach
		/// the shooter (including in multiplayer) and go through the same bonus calculation
		/// (KillScoreCalculator.GetExplosionBonuses) as with dynamite.
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
				// Deliberately read TotalWorth before SetKillscoreMultiplier - as in the original.
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
			Plugin.Log.LogInfo($"Explosion kill: {names} (+{worth})");
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
		/// Dynamite-in-water effect: the detonation floats dead fish up to the surface.
		/// These count as kills just like with dynamite, so they go into the killscore list.
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