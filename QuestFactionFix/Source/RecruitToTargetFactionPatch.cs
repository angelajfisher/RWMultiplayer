using HarmonyLib;
using RimWorld;
using Verse;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(QuestPart_JoinPlayer), "Notify_QuestSignalReceived")]
internal static class RecruitToTargetFactionPatch
{
	private static void Prefix(QuestPart_JoinPlayer __instance, Signal signal, ref QuestFactionScope __state)
	{
		if (QuestOwnership.Enabled && !(signal.tag != __instance.inSignal))
		{
			Faction faction = __instance.mapParent?.Faction;
			if (!QuestOwnership.IsColonyFaction(faction))
			{
				faction = QuestOwnership.Owner(__instance.quest);
			}
			__state = QuestFactionScope.Enter(faction, __instance.mapParent?.Map);
			if (__state != null)
			{
				Log.Message($"[MP Quest Faction Fix] Recruitment signal {signal.tag}: using colony {faction.Name} ({faction.loadID}).");
			}
		}
	}

	private static void Finalizer(QuestFactionScope __state)
	{
		__state?.Dispose();
	}
}
