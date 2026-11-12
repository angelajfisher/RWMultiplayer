using HarmonyLib;
using Verse;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(LetterWithTimeout), "ShouldAutomaticallyOpenLetter", MethodType.Getter)]
internal static class PrivateChoiceAutoOpenPatch
{
	private static void Postfix(LetterWithTimeout __instance, ref bool __result)
	{
		if (!ActionableLetters.VisibleToViewer(__instance))
		{
			__result = false;
		}
	}
}
