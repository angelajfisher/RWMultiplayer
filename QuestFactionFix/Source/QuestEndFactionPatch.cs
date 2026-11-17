using HarmonyLib;
using RimWorld;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(Quest), "End")]
internal static class QuestEndFactionPatch
{
	private static void Prefix(Quest __instance, ref QuestFactionScope __state)
	{
		if (QuestOwnership.Enabled)
		{
			__state = QuestOwnership.Enter(__instance);
		}
	}

	private static void Finalizer(QuestFactionScope __state)
	{
		__state?.Dispose();
	}
}
