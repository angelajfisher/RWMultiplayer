using HarmonyLib;
using RimWorld;
using MP = Multiplayer.Client.Multiplayer;

namespace MultiplayerQuestFactionFix;

// Run before MP's quest-map context switch. Replay decisions must use the
// issuing command's faction, never the local viewer on each machine.
[HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
internal static class QuestAcceptancePatch
{
    [HarmonyPriority(Priority.First + 100)]
    private static bool Prefix(Quest __instance)
    {
        if (!QuestOwnership.Enabled || (!MP.InInterface && !MP.ExecutingCmds)) return true;
        return CanAccept(__instance, MP.InInterface ? MP.RealPlayerFaction : Faction.OfPlayer);
    }

    public static bool CanAccept(Quest quest, Faction actor)
    {
        if (!QuestOwnership.IsColonyFaction(actor)) return false;
        var owner = QuestOwnership.Owner(quest);
        // Sharing uses separate invitation/participation actions. It must not
        // authorize an invitee to start the owner's unaccepted quest.
        return owner == null || owner == actor;
    }
}
