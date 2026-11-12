using HarmonyLib;
using RimWorld;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(QuestPart_SetFaction), "Notify_QuestSignalReceived")]
internal static class CreepJoinerFactionPatch
{
	internal sealed class State
	{
		internal Faction previous;

		internal QuestFactionScope scope;
	}

	private static void Prefix(QuestPart_SetFaction __instance, Signal signal, ref State __state)
	{
		if (!QuestOwnership.Enabled || signal.tag != __instance.inSignal || __instance.quest?.root?.defName?.StartsWith("CreepJoinerArrival") != true)
		{
			return;
		}
		Faction faction = __instance.faction;
		if (faction != null && faction.def?.isPlayer == true)
		{
			Faction faction2 = QuestOwnership.Owner(__instance.quest);
			if (QuestOwnership.IsColonyFaction(faction2))
			{
				__state = new State
				{
					previous = __instance.faction
				};
				__state.scope = QuestFactionScope.Enter(faction2, QuestOwnership.TargetMap(__instance.quest));
				__instance.faction = faction2;
			}
		}
	}

	private static void Finalizer(QuestPart_SetFaction __instance, State __state)
	{
		if (__state != null)
		{
			__instance.faction = __state.previous;
			__state.scope?.Dispose();
		}
	}
}
