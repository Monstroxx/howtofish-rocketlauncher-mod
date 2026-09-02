using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace RocketLauncherMod
{
	/// <summary>
	/// Homing missiles upgrade for the Rocket Launcher.
	/// - While aiming (ADS), the crosshair locks onto the target closest to the camera
	///   (living fish, seagulls/birds). The lock box colors itself red -> orange -> green.
	/// - With a green lock, fired rockets track the target server-side (host).
	/// - Purchased via /rocketbuy (cost goes to the server treasury, like shop attachments).
	/// </summary>
	public static class HomingMissiles
	{
		private const string AttachmentName = "Homing Missiles";

		// Lock progress per second with a valid target; 0..1, green from 1 upward.
		private const float LockTimeSeconds = 0.75f;

		// Maximum angle (degrees) between camera view and target for the lock to keep counting.
		private const float MaxLockAngle = 14f;

		// Maximum distance (m) beyond which locking doesn't happen at all.
		private const float MaxLockRange = 90f;

		// Max. direction change of the rocket per second (degrees) - prevents a perfect "always hits".
		private const float MaxTurnDegreesPerSecond = 190f;

		public static ConfigEntry<int> CfgCost;

		public static bool Purchased;

		// Local: current lock target + progress (shooter-only, UI).
		private static Transform _lockTarget;

		private static float _lockProgress;

		private static float _wasAdsTime;

		// Server: flying rockets + their targets.
		private static readonly List<Projectile> _homingRockets = new List<Projectile>();

		private static readonly List<Creature> _serverTargets = new List<Creature>();

		// UI box on the target (local object, tracked via LateUpdate).
		private static GameObject _lockBox;
		private static MeshRenderer _lockBoxRenderer;
		private static Material _lockBoxMaterial;

		private static readonly Color ColorRed = new Color(0.9f, 0.1f, 0.1f);
		private static readonly Color ColorOrange = new Color(1f, 0.55f, 0.05f);
		private static readonly Color ColorGreen = new Color(0.1f, 0.95f, 0.2f);

		public static void Bind(ConfigFile config)
		{
			CfgCost = config.Bind("Homing", "Cost", 5000,
				"Purchase price for the Homing Missiles upgrade (/rocketbuy).");
		}

		public static bool IsHomingReady => Purchased && Plugin.RocketTypeReady;

		/// <summary>Current lock progress 0..1 (1 = green / firm lock).</summary>
		public static float LockProgress => _lockProgress;

		public static Transform LockTarget => _lockTarget;

		/// <summary>Called when a rocket spawns: with a firm lock (green) it becomes homing.
		/// The shooter knows their lock target locally; the host then steers the rocket
		/// server-side (in solo/host play, both are the same machine).</summary>
		public static void OnProjectileSpawned(Projectile projectile, bool isLocal, Player owner)
		{
			if (!Purchased || projectile == null)
			{
				return;
			}
			if (owner == null || owner.Owner == null || !owner.Owner.IsLocalClient)
			{
				return;
			}
			if (_lockTarget == null || _lockProgress < 1f)
			{
				return;
			}
			Item targetItem = ItemManager.Get(_lockTarget);
			if (targetItem == null || targetItem.Creature == null)
			{
				return;
			}
			RegisterServerHoming(projectile, targetItem.Creature);
			Plugin.Log.LogInfo($"Homing rocket launched -> {targetItem.Creature.GetName()}");
		}

		public static void RegisterServerHoming(Projectile projectile, Creature target)
		{
			if (projectile == null || target == null)
			{
				return;
			}
			if (!_homingRockets.Contains(projectile))
			{
				_homingRockets.Add(projectile);
				_serverTargets.Add(target);
			}
		}

		/// <summary>FixedUpdate postfix: steers homing rockets toward their target (server + local preview).</summary>
		public static void SteerProjectiles(ProjectileManager manager)
		{
			if (_homingRockets.Count == 0)
			{
				return;
			}
			for (int i = _homingRockets.Count - 1; i >= 0; i--)
			{
				Projectile projectile = _homingRockets[i];
				Creature target = _serverTargets[i];
				ProjectileType type = manager.GetType(Plugin.RocketTypeId);
				if (!type.Projectiles.Contains(projectile) || projectile.Velocity.sqrMagnitude < 0.01f)
				{
					_homingRockets.RemoveAt(i);
					_serverTargets.RemoveAt(i);
					continue;
				}
				// Target gone/despawned -> rocket keeps flying, without steering.
				if (!target || target.IsDead || !target.transform)
				{
					_homingRockets.RemoveAt(i);
					_serverTargets.RemoveAt(i);
					continue;
				}
				Vector3 toTarget = target.transform.position - projectile.Position;
				Vector3 dir = projectile.Velocity.normalized;
				Vector3 want = toTarget.normalized;
				float maxTurn = MaxTurnDegreesPerSecond * Time.fixedDeltaTime;
				Vector3 steered = Vector3.RotateTowards(dir, want, maxTurn * Mathf.Deg2Rad, 0f);
				float speed = projectile.Velocity.magnitude;
				projectile.Velocity = steered * speed;
			}
		}

		/// <summary>Called when a projectile has been removed (hit/fuse) - remove its entry.</summary>
		public static void OnProjectileGone(Projectile projectile)
		{
			int index = _homingRockets.IndexOf(projectile);
			if (index >= 0)
			{
				_homingRockets.RemoveAt(index);
				_serverTargets.RemoveAt(index);
			}
		}

		/// <summary>Finds the best lock target near the camera (fish + seagulls, alive).</summary>
		private static Creature FindBestTarget()
		{
			Player local = Player.LocalPlayer;
			if (!local || !local.CamObject || !GameInfo.CurCamera)
			{
				return null;
			}
			Vector3 camPos = local.CamObject.position;
			Vector3 camDir = local.CamObject.forward;
			float bestScore = float.MaxValue;
			Creature best = null;
			foreach (KeyValuePair<Transform, Item> pair in ItemManager.Items)
			{
				Item item = pair.Value;
				if (!item || item.IsDeinitializing)
				{
					continue;
				}
				Creature creature = item.Creature;
				if (!creature || creature.IsDead || !creature.transform)
				{
					continue;
				}
				Vector3 pos = creature.transform.position;
				Vector3 toTarget = pos - camPos;
				float dist = toTarget.magnitude;
				if (dist < 1.5f || dist > MaxLockRange)
				{
					continue;
				}
				float angle = Vector3.Angle(camDir, toTarget);
				// Closer to screen center = smaller angle; this is the "closest to the crosshair" check.
				if (angle > MaxLockAngle)
				{
					continue;
				}
				// Targets underwater are fine (fish); seagulls fly in the open anyway.
				float score = angle + dist * 0.05f;
				if (score < bestScore)
				{
					bestScore = score;
					best = creature;
				}
			}
			return best;
		}

		/// <summary>Called every frame by the plugin (local only, display + lock state).</summary>
		public static void UpdateLocalLock(bool isAds)
		{
			if (!Purchased)
			{
				EnsureLockBoxVisible(false);
				return;
			}
			// Leaving ADS -> reset the lock. Short grace period so a shot mid-ADS doesn't
			// instantly kill the lock (IsAds can briefly be false).
			if (!isAds)
			{
				if (_wasAdsTime > 0f && Time.time - _wasAdsTime < 0.25f)
				{
					// let it keep running briefly (the shot frame)
				}
				else
				{
					_lockTarget = null;
					_lockProgress = 0f;
					EnsureLockBoxVisible(false);
					return;
				}
			}
			else
			{
				_wasAdsTime = Time.time;
			}
			Creature target = FindBestTarget();
			if (target != null && target.transform == _lockTarget)
			{
				_lockProgress = Mathf.Min(1f, _lockProgress + Time.deltaTime / LockTimeSeconds);
			}
			else if (target != null)
			{
				// New target -> rebuild the lock, but only if we didn't already have a firm lock.
				if (_lockProgress >= 1f)
				{
					// Keep the firm lock as long as the target is still within the cone.
					if (IsStillInCone(_lockTarget))
					{
						EnsureLockBoxVisible(true);
						UpdateLockBox();
						return;
					}
					_lockProgress = 0f;
				}
				_lockTarget = target.transform;
				_lockProgress = Mathf.Min(_lockProgress + Time.deltaTime / LockTimeSeconds, 1f);
			}
			else
			{
				// No target in the cone: let the lock decay slowly.
				_lockProgress = Mathf.Max(0f, _lockProgress - Time.deltaTime / LockTimeSeconds * 1.5f);
				if (_lockProgress <= 0f)
				{
					_lockTarget = null;
				}
			}
			EnsureLockBoxVisible(_lockTarget != null && _lockProgress > 0f);
			UpdateLockBox();
		}

		private static bool IsStillInCone(Transform target)
		{
			if (!target || !Player.LocalPlayer || !Player.LocalPlayer.CamObject)
			{
				return false;
			}
			Vector3 toTarget = target.position - Player.LocalPlayer.CamObject.position;
			return toTarget.magnitude <= MaxLockRange && Vector3.Angle(Player.LocalPlayer.CamObject.forward, toTarget) <= MaxLockAngle * 1.5f;
		}

		private static void EnsureLockBoxVisible(bool visible)
		{
			if (visible && _lockBox == null)
			{
				// Check the SHADER, not the material: new Material(...) never returns null,
				// so the old fallback could never trigger - with URP Unlit missing, you got
				// a material with a null shader (magenta lock box). Build the material first
				// so UpdateLockBox never finds a box without a material.
				Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
				if (shader == null)
				{
					shader = Shader.Find("Sprites/Default");
				}
				if (shader == null)
				{
					Plugin.Log.LogWarning("No shader found for the lock box - homing lock stays invisible.");
					return;
				}
				_lockBoxMaterial = new Material(shader);
				_lockBoxMaterial.enableInstancing = false;
				_lockBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
				_lockBox.name = "RocketLockBox";
				UnityEngine.Object.DontDestroyOnLoad(_lockBox);
				UnityEngine.Object.Destroy(_lockBox.GetComponent<Collider>());
				_lockBoxRenderer = _lockBox.GetComponent<MeshRenderer>();
				_lockBoxRenderer.sharedMaterial = _lockBoxMaterial;
			}
			if (_lockBox != null && _lockBox.activeSelf != visible)
			{
				_lockBox.SetActive(visible);
			}
		}

		private static void UpdateLockBox()
		{
			if (_lockBox == null || _lockTarget == null)
			{
				return;
			}
			Bounds bounds = GetTargetBounds(_lockTarget);
			Vector3 size = bounds.size;
			float extent = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
			// Box slightly larger than the target, min. 0.8 m.
			float boxSize = Mathf.Max(0.8f, extent * 1.35f);
			_lockBox.transform.position = bounds.center;
			_lockBox.transform.localScale = new Vector3(boxSize, boxSize, boxSize);
			// Wireframe look: outline only - kept simple via color + transparency.
			Color color = _lockProgress >= 1f ? ColorGreen
				: (_lockProgress >= 0.5f ? Color.Lerp(ColorOrange, ColorGreen, (_lockProgress - 0.5f) * 2f)
					: Color.Lerp(ColorRed, ColorOrange, _lockProgress * 2f));
			if (_lockBoxMaterial.HasProperty("_BaseColor"))
			{
				_lockBoxMaterial.SetColor("_BaseColor", color);
			}
			if (_lockBoxMaterial.HasProperty("_Color"))
			{
				_lockBoxMaterial.SetColor("_Color", color);
			}
		}

		private static Bounds GetTargetBounds(Transform target)
		{
			Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
			if (renderers.Length == 0)
			{
				return new Bounds(target.position, Vector3.one);
			}
			Bounds bounds = renderers[0].bounds;
			for (int i = 1; i < renderers.Length; i++)
			{
				bounds.Encapsulate(renderers[i].bounds);
			}
			return bounds;
		}

		/// <summary>/rocketbuy - purchase of the upgrade, cost goes to the server treasury.</summary>
		public static void Buy(Player buyer)
		{
			if (Purchased)
			{
				ChatManager.ChatMessage("[Rocket] Homing Missiles are already purchased!");
				return;
			}
			if (CfgCost.Value > 0)
			{
				if (!MoneyManager.CanAfford(CfgCost.Value))
				{
					ChatManager.ChatMessage($"[Rocket] Not enough money - Homing Missiles cost ${CfgCost.Value}");
					return;
				}
				MoneyManager.RemoveMoney(CfgCost.Value, buyer);
			}
			Purchased = true;
			ChatManager.ChatMessage($"[Rocket] Homing Missiles purchased! (${CfgCost.Value}) Aim (ADS), keep the target in the crosshair until the box turns green, then shoot.");
			Plugin.Log.LogInfo("Homing Missiles purchased");
		}

		/// <summary>Reset after a save load or a new game.</summary>
		public static void Reset()
		{
			Purchased = false;
			_lockTarget = null;
			_lockProgress = 0f;
			_homingRockets.Clear();
			_serverTargets.Clear();
		}
	}
}