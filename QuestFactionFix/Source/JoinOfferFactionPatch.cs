using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(QuestPart_PawnJoinOffer), "SendLetter")]
internal static class JoinOfferFactionPatch
{
	private static void Prefix(QuestPart_PawnJoinOffer __instance, ref QuestFactionScope __state)
	{
		if (QuestOwnership.Enabled)
		{
			MapParent mapParent = QuestOwnership.JoinOfferTarget(__instance);
			Map map = mapParent?.Map ?? __instance.pawn?.MapHeld;
			Faction faction = mapParent?.Faction ?? QuestOwnership.Owner(__instance.quest) ?? map?.ParentFaction;
			__state = QuestFactionScope.Enter(faction, map);
			if (__state != null)
			{
				Log.Message($"[MP Quest Faction Fix] Join offer for {__instance.pawn?.ThingID}: quest {__instance.quest?.id} routed to {faction.Name} ({faction.loadID}).");
			}
		}
	}

	private static void Finalizer(QuestFactionScope __state)
	{
		__state?.Dispose();
	}
}
