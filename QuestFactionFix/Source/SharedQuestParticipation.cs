using System;
using HarmonyLib;
using RimWorld;

namespace MultiplayerQuestFactionFix;

internal static class SharedQuestParticipation
{
	public static bool IsParticipant(Quest quest, Faction faction) => HasAccess(quest, faction, false);

	public static bool CanView(Quest quest, Faction faction) => HasAccess(quest, faction, true);

	private static bool HasAccess(Quest quest, Faction faction, bool invitations)
	{
		Type type = AccessTools.TypeByName("RaidAnimalFix.SharedQuestUtil");
		if (type == null)
		{
			return false;
		}
		object obj = AccessTools.Method(type, "EntryFor")?.Invoke(null, new object[1] { quest });
		if (obj != null)
		{
			return (bool)AccessTools.Method(obj.GetType(), "IsParticipant").Invoke(obj, new object[] { faction }) ||
                (invitations && (bool)AccessTools.Method(obj.GetType(), "IsInvited").Invoke(obj, new object[] { faction }));
		}
		return false;
	}
}
