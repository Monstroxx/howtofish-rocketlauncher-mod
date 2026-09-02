using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace RocketLauncherMod
{
	/// <summary>
	/// Homing-Missiles-Upgrade fuer den Rocket Launcher.
	/// - Beim Zielen (ADS) lockt das Fadenkreuz das Ziel, das der Kamera am naechsten ist
	///   (lebende Fische, Moewen/Birds). Die Lock-Box faerbt sich rot -> orange -> gruen.
	/// - Bei gruenem Lock feuerte Raketen verfolgen das Ziel serverseitig (host).
	/// - Gekauft per /rocketbuy (Kosten an die Serverkasse, wie Shop-Attachments).
	/// </summary>
	public static class HomingMissiles
	{
		private const string AttachmentName = "Homing Missiles";

		// Lock-Progress pro Sekunde bei gueltigem Ziel; 0..1, gruen ab 1.
		private const float LockTimeSeconds = 0.75f;

		// Maximaler Winkel (Grad) zwischen Kamera-Blick und Ziel, damit Lock weitergezaehlt wird.
		private const float MaxLockAngle = 14f;

		// Maximaler Abstand (m), ab dem gar nicht mehr gelockt wird.
		private const float MaxLockRange = 90f;

		// Max. Richtungs-Aenderung der Rakete pro Sekunde (Grad) - verhindert perfektes "immer trifft".
		private const float MaxTurnDegreesPerSecond = 190f;

		public static ConfigEntry<int> CfgCost;

		public static bool Purchased;

		// Lokal: aktuelles Lock-Ziel + Fortschritt (nur Schuetze, UI).
		private static Transform _lockTarget;

		private static float _lockProgress;

		private static float _wasAdsTime;

		// Server: fliegende Raketen + ihre Ziele.
		private static readonly List<Projectile> _homingRockets = new List<Projectile>();

		private static readonly List<Creature> _serverTargets = new List<Creature>();

		// UI-Box am Ziel (lokales Objekt, wird per LateUpdate nachgezogen).
		private static GameObject _lockBox;
		private static MeshRenderer _lockBoxRenderer;
		private static Material _lockBoxMaterial;

		private static readonly Color ColorRed = new Color(0.9f, 0.1f, 0.1f);
		private static readonly Color ColorOrange = new Color(1f, 0.55f, 0.05f);
		private static readonly Color ColorGreen = new Color(0.1f, 0.95f, 0.2f);

		public static void Bind(ConfigFile config)
		{
			CfgCost = config.Bind("Homing", "Cost", 5000,
				"Kaufpreis fuer das Homing-Missiles-Upgrade (/rocketbuy).");
		}

		public static bool IsHomingReady => Purchased && Plugin.RocketTypeReady;

		/// <summary>Aktueller Lock-Fortschritt 0..1 (1 = gruen / festes Schloss).</summary>
		public static float LockProgress => _lockProgress;

		public static Transform LockTarget => _lockTarget;

		/// <summary>Wird beim Spawnen einer Rakete gerufen: Bei festem Lock (gruen) wird sie homing.
		/// Der Schuetze kennt sein Lock-Ziel lokal; der Host steuert die Rakete dann serverseitig
		/// (beim Solo-/Host-Spiel beides dieselbe Maschine).</summary>
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
			Plugin.Log.LogInfo($"Homing-Rakete gestartet -> {targetItem.Creature.GetName()}");
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

		/// <summary>FixUpdate-Postfix: lenkt homing Raketen Richtung Ziel (server + local preview).</summary>
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
				// Ziel weg/lokalisiert -> Rakete fliegt weiter, ohne Steuerung.
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

		/// <summary>Aufgerufen, wenn ein Projektil entfernt wurde (Hit/Fuse) - Eintrag entfernen.</summary>
		public static void OnProjectileGone(Projectile projectile)
		{
			int index = _homingRockets.IndexOf(projectile);
			if (index >= 0)
			{
				_homingRockets.RemoveAt(index);
				_serverTargets.RemoveAt(index);
			}
		}

		/// <summary>Findet das beste Lock-Ziel in Kameranaehe (Fische + Moewen, lebendig).</summary>
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
				// Naeher am Bildschirmzentrum = kleinerer Winkel; das "am naechsten am Fadenkreuz".
				if (angle > MaxLockAngle)
				{
					continue;
				}
				// Ziele im Wasser unterhalb der Wasseroberflaeche sind ok (Fische), Moewen fliegen eh offen.
				float score = angle + dist * 0.05f;
				if (score < bestScore)
				{
					bestScore = score;
					best = creature;
				}
			}
			return best;
		}

		/// <summary>Wird pro Frame vom Plugin gerufen (lokal, nur Anzeige + Lock-Zustand).</summary>
		public static void UpdateLocalLock(bool isAds)
		{
			if (!Purchased)
			{
				EnsureLockBoxVisible(false);
				return;
			}
			// ADS verlassen -> Lock resetten. Kurze Nachlaufzeit, damit ein Schuss
			// mitten im ADS das Lock nicht sofort killt (IsAds kann kurz false sein).
			if (!isAds)
			{
				if (_wasAdsTime > 0f && Time.time - _wasAdsTime < 0.25f)
				{
					// kurz weiterlaufen lassen (Schuss-Frame)
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
				// Neues Ziel -> Lock neu aufbauen, aber nur wenn wir vorher kein festes Lock hatten.
				if (_lockProgress >= 1f)
				{
					// festes Lock behalten, solange das Ziel noch im Kegel ist.
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
				// Kein Ziel im Kegel: Lock langsam verfallen lassen.
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
				// Den SHADER pruefen, nicht das Material: new Material(...) liefert nie null,
				// der alte Fallback konnte also nie greifen - bei fehlendem URP-Unlit gab es
				// ein Material mit null-Shader (magenta Lock-Box). Zuerst das Material bauen,
				// damit UpdateLockBox nie eine Box ohne Material vorfindet.
				Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
				if (shader == null)
				{
					shader = Shader.Find("Sprites/Default");
				}
				if (shader == null)
				{
					Plugin.Log.LogWarning("Kein Shader fuer die Lock-Box gefunden - Homing-Lock bleibt unsichtbar.");
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
			// Box etwas groesser als das Ziel, min. 0.8 m.
			float boxSize = Mathf.Max(0.8f, extent * 1.35f);
			_lockBox.transform.position = bounds.center;
			_lockBox.transform.localScale = new Vector3(boxSize, boxSize, boxSize);
			// Wireframe-Look: nur Kontur - via Farbe + Transparenz einfach gehalten.
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

		/// <summary>/rocketbuy - Kauf des Upgrades, Kosten gehen an die Serverkasse.</summary>
		public static void Buy(Player buyer)
		{
			if (Purchased)
			{
				ChatManager.ChatMessage("[Rocket] Homing Missiles sind bereits gekauft!");
				return;
			}
			if (CfgCost.Value > 0)
			{
				if (!MoneyManager.CanAfford(CfgCost.Value))
				{
					ChatManager.ChatMessage($"[Rocket] Nicht genug Geld - Homing Missiles kosten ${CfgCost.Value}");
					return;
				}
				MoneyManager.RemoveMoney(CfgCost.Value, buyer);
			}
			Purchased = true;
			ChatManager.ChatMessage($"[Rocket] Homing Missiles gekauft! (${CfgCost.Value}) Zielen (ADS), Ziel im Fadenkreuz halten bis die Box gruen wird, dann schiessen.");
			Plugin.Log.LogInfo("Homing Missiles gekauft");
		}

		/// <summary>Nach Save-Load oder neuem Spiel zuruecksetzen.</summary>
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