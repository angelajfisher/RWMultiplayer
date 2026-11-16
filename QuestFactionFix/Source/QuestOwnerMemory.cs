using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MultiplayerQuestFactionFix;

// Quest cleanup can remove the very map/pawn references used to infer ownership.
// Only simulation lifecycle hooks record it; UI queries never write this state.
public static class QuestOwnerMemory
{
    private sealed class Record { public Record() { } internal int factionId = -1; }
    private static readonly ConditionalWeakTable<Quest, Record> records = new();
    public static Faction Owner(Quest quest)
    {
        if (quest == null || !records.TryGetValue(quest, out var record) || record.factionId < 0) return null;
        foreach (var faction in Find.FactionManager.AllFactionsListForReading)
            if (faction.loadID == record.factionId) return faction;
        return null;
    }
    public static void Remember(Quest quest)
    {
        if (!QuestOwnership.Enabled || quest == null) return;
        var owner = QuestOwnership.Owner(quest);
        records.GetOrCreateValue(quest).factionId = QuestOwnership.IsColonyFaction(owner) ? owner.loadID : -1;
    }
    public static void Expose(Quest quest)
    {
        var record = records.GetOrCreateValue(quest);
        Scribe_Values.Look(ref record.factionId, "questFactionFixOwner", -1);
    }
}

[HarmonyPatch]
internal static class RememberQuestOwnerBeforeCleanup
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Quest), nameof(Quest.End));
        yield return AccessTools.Method(typeof(Quest), nameof(Quest.CleanupQuestParts));
    }
    [HarmonyPriority(Priority.First + 200)]
    private static void Prefix(Quest __instance) => QuestOwnerMemory.Remember(__instance);
}

[HarmonyPatch(typeof(QuestManager), nameof(QuestManager.Add))]
internal static class RememberNewQuestOwner
{
    private static void Prefix(Quest quest) => QuestOwnerMemory.Remember(quest);
}

[HarmonyPatch(typeof(Quest), nameof(Quest.ExposeData))]
internal static class SaveQuestOwner
{
    private static void Postfix(Quest __instance) => QuestOwnerMemory.Expose(__instance);
}
