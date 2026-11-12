using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Client.Comp;
using RimWorld;

namespace MultiplayerQuestFactionFix;

[HarmonyPatch(typeof(MultiplayerAsyncQuest), "TryGetQuestMap")]
internal static class QuestMapLookupPatch
{
	private static bool Prefix(Quest quest, ref AsyncTimeComp __result)
	{
		if (!QuestOwnership.Enabled)
		{
			return true;
		}
		__result = QuestOwnership.FindHomeTime(quest);
		return false;
	}
}
