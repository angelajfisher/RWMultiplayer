using HarmonyLib;
using RimWorld;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(Quest), "QuestTick")]
internal static class QuestTickFactionPatch
{
	private static void Prefix(Quest __instance, ref QuestFactionScope __state)
	{
		if (QuestOwnership.Enabled)
		{
			__state = QuestFactionScope.Enter(QuestOwnership.Owner(__instance), QuestOwnership.TargetMap(__instance));
		}
	}

	private static void Finalizer(QuestFactionScope __state)
	{
		__state?.Dispose();
	}
}
