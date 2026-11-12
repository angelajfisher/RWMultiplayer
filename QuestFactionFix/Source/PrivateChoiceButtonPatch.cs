using HarmonyLib;
using Verse;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(Letter), "DrawButtonAt")]
internal static class PrivateChoiceButtonPatch
{
	private static bool Prefix(Letter __instance)
	{
		return ActionableLetters.VisibleToViewer(__instance);
	}
}
