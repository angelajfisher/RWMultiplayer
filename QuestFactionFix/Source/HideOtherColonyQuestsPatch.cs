using HarmonyLib;
using Multiplayer.Client;
using RimWorld;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(MainTabWindow_Quests), "ShouldListNow")]
internal static class HideOtherColonyQuestsPatch
{
	[HarmonyPriority(800)]
	private static bool Prefix(Quest quest, ref bool __result)
	{
		if (!QuestOwnership.Enabled || QuestOwnership.ShouldShow(quest, Multiplayer.Client.Multiplayer.RealPlayerFaction))
		{
			return true;
		}
		__result = false;
		return false;
	}
}
