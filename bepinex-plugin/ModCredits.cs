using System;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RocketLauncherMod
{
	/// <summary>
	/// Adds the mod's own section to the game's credits - both the scrolling
	/// credits screen (CreditsScroll) and the end-game roll (EndGameUI).
	///
	/// The section is built by cloning an existing credits section and swapping
	/// its text, so it inherits the game's font, size, color and spacing instead
	/// of us guessing at them. The credits layout lives in the scene, not in
	/// code, so everything here is discovered at runtime and falls back to a
	/// warning in the log rather than throwing.
	/// </summary>
	public static class ModCredits
	{
		/// <summary>Name of the injected object, and the "already injected" marker.</summary>
		private const string MarkerName = "RocketLauncherModCredits";

		/// <summary>Gap to the section above, in pixels, when the credits container
		/// has no layout group doing the spacing for us.</summary>
		private const float ManualGap = 40f;

		/// <summary>One line per text object of the cloned section: the first one
		/// lands in the section's title, the rest in its name lines.</summary>
		private static readonly string[] Lines =
		{
			"Rocket Launcher Mod",
			"Monstroxx",
			"3D Assets: Hans Woofington (Asimov 3D Guns Pack)"
		};

		public static ConfigEntry<bool> CfgShowCredits;

		public static void Bind(ConfigFile config)
		{
			CfgShowCredits = config.Bind("General", "ShowModCredits", true,
				"Adds a Rocket Launcher Mod section to the game's credits.");
		}

		/// <summary>Appends the mod section to a credits container. Safe to call on
		/// every open: the marker object makes it a no-op the second time.</summary>
		public static void Inject(RectTransform content, string source)
		{
			if (content == null || (CfgShowCredits != null && !CfgShowCredits.Value))
			{
				return;
			}
			try
			{
				if (content.Find(MarkerName) != null)
				{
					return;
				}
				RectTransform template = FindSectionTemplate(content);
				if (template == null)
				{
					Plugin.Log.LogWarning($"Credits ({source}): found no section to clone - mod credits skipped.");
					return;
				}
				GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, content);
				clone.name = MarkerName;
				clone.SetActive(true);
				StripOverwritingComponents(clone);
				ApplyLines(clone);
				Place(content, template, (RectTransform)clone.transform);
				Plugin.Log.LogInfo($"Credits ({source}): mod section added (cloned from '{template.name}').");
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError($"Credits ({source}): injection failed: {ex}");
			}
		}

		/// <summary>The last direct child that holds text - the bottom-most section
		/// of the roll, and the one whose styling a new section should match.</summary>
		private static RectTransform FindSectionTemplate(RectTransform content)
		{
			for (int i = content.childCount - 1; i >= 0; i--)
			{
				RectTransform child = content.GetChild(i) as RectTransform;
				if (child != null && child.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true) != null)
				{
					return child;
				}
			}
			return null;
		}

		/// <summary>CreditsTitle title-cases the text on enable and Unity's
		/// LocalizeStringEvent rewrites it from the string table - both would undo
		/// our text, so the clone keeps neither.</summary>
		private static void StripOverwritingComponents(GameObject clone)
		{
			foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
			{
				if (behaviour == null)
				{
					continue;
				}
				if (behaviour is CreditsTitle
					|| behaviour.GetType().Name.IndexOf("Localize", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					UnityEngine.Object.DestroyImmediate(behaviour);
				}
			}
		}

		private static void ApplyLines(GameObject clone)
		{
			TextMeshProUGUI[] texts = clone.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true);
			if (texts.Length == 0)
			{
				return;
			}
			// Sections differ in how many name lines they hold. With fewer text
			// objects than lines, everything goes into the first one - TMP renders
			// the newlines - rather than cloning line objects whose position would
			// then have to be guessed at.
			if (texts.Length < Lines.Length)
			{
				texts[0].gameObject.SetActive(true);
				texts[0].text = string.Join("\n", Lines);
				for (int i = 1; i < texts.Length; i++)
				{
					texts[i].gameObject.SetActive(false);
				}
				return;
			}
			for (int i = 0; i < texts.Length; i++)
			{
				if (i < Lines.Length)
				{
					texts[i].gameObject.SetActive(true);
					texts[i].text = Lines[i];
				}
				else
				{
					texts[i].gameObject.SetActive(false);
				}
			}
		}

		private static void Place(RectTransform content, RectTransform template, RectTransform clone)
		{
			clone.SetAsLastSibling();
			if (content.GetComponent<LayoutGroup>() != null)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(content);
				// A ContentSizeFitter already grew the container for the new child.
				if (content.GetComponent<ContentSizeFitter>() == null)
				{
					Grow(content, template.rect.height + ManualGap);
				}
				return;
			}
			// No layout group: park the section below the lowest existing one.
			float lowestBottom = float.MaxValue;
			for (int i = 0; i < content.childCount; i++)
			{
				RectTransform child = content.GetChild(i) as RectTransform;
				if (child == null || child == clone)
				{
					continue;
				}
				float bottom = child.anchoredPosition.y - child.rect.height * child.pivot.y;
				if (bottom < lowestBottom)
				{
					lowestBottom = bottom;
				}
			}
			if (lowestBottom == float.MaxValue)
			{
				lowestBottom = 0f;
			}
			float height = (clone.rect.height > 0f) ? clone.rect.height : template.rect.height;
			float top = lowestBottom - ManualGap;
			clone.anchoredPosition = new Vector2(template.anchoredPosition.x, top - height * (1f - clone.pivot.y));
			Grow(content, height + ManualGap);
		}

		/// <summary>Both credits rolls scroll by the container's sizeDelta.y - without
		/// growing it, the added section would scroll off past the end of the roll.</summary>
		private static void Grow(RectTransform content, float amount)
		{
			content.sizeDelta = new Vector2(content.sizeDelta.x, content.sizeDelta.y + amount);
		}

		/// <summary>
		/// Patched separately from Plugin.Patches, and deliberately so: a failing
		/// PatchAll throws, and in Plugin.Start that aborts the whole mod. The
		/// credits are cosmetic, so if a game update renames these members, the
		/// launcher itself has to keep working.
		/// </summary>
		public static class CreditsPatches
		{
			/// <summary>
			/// Credits screen. Prefix, not postfix: OnEnable goes straight into
			/// ScrollCredits, which reads sizeDelta.y to compute the scroll
			/// distance - the section has to be in place before that.
			/// </summary>
			[HarmonyPatch(typeof(CreditsScroll), "OnEnable")]
			[HarmonyPrefix]
			public static void CreditsScroll_OnEnable_Prefix(CreditsScroll __instance)
			{
				RectTransform content = (RectTransform)AccessTools.Field(typeof(CreditsScroll), "_rect").GetValue(__instance);
				Inject(content, "credits screen");
			}

			/// <summary>End-game credits roll. ShowCredits runs before
			/// StartScrollingCredits does the same sizeDelta math.</summary>
			[HarmonyPatch(typeof(EndGameUI), "ShowCredits")]
			[HarmonyPrefix]
			public static void EndGameUI_ShowCredits_Prefix(EndGameUI __instance)
			{
				RectTransform content = (RectTransform)AccessTools.Field(typeof(EndGameUI), "_creditsHolder").GetValue(__instance);
				Inject(content, "end-game credits");
			}
		}
	}
}
