using HarmonyLib;
using Multiplayer.Client.Factions;
using RimWorld;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(FactionExtensions), "TryGetPlayerFaction")]
internal static class QuestOwnerLookupPatch
{
	private static bool Prefix(Quest quest, ref Faction faction, ref bool __result)
	{
		if (!QuestOwnership.Enabled)
		{
			return true;
		}
		faction = QuestOwnership.Owner(quest);
		__result = faction != null;
		return false;
	}
}
