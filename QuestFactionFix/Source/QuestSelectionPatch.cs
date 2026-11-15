using HarmonyLib;
using RimWorld;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

namespace MultiplayerQuestFactionFix;

// Event lists and archived notifications can select a quest without consulting
// ShouldListNow. Guard that entry point as well as the visible list.
[HarmonyPatch(typeof(MainTabWindow_Quests), "Select")]
internal static class QuestSelectionPatch
{
    private static bool Prefix(Quest quest) => !QuestOwnership.Enabled || quest == null ||
        QuestOwnership.ShouldShow(quest, MP.RealPlayerFaction);
}

[HarmonyPatch(typeof(NewQuestLetter), nameof(NewQuestLetter.OpenLetter))]
internal static class QuestOfferOpenPatch
{
    private static bool Prefix(NewQuestLetter __instance) => !QuestOwnership.Enabled ||
        QuestOwnership.ShouldShow(__instance.quest, MP.RealPlayerFaction);
}

// Stop before reward selection/pre-accept callbacks, not only at Quest.Accept.
[HarmonyPatch]
internal static class QuestAcceptanceInterfacePatch
{
    private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MainTabWindow_Quests), "AcceptQuestByInterface");
        yield return AccessTools.Method(typeof(MainTabWindow_Quests), "DoAcceptButton");
    }
    private static bool Prefix(Quest ___selected) => !QuestOwnership.Enabled ||
        QuestAcceptancePatch.CanAccept(___selected, MP.RealPlayerFaction);
}
