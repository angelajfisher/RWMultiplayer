using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace IdeologyFactionFix;

public sealed class FactionTreeHistory : IExposable
{
    public Faction faction;
    public List<int> ticks = new();
    public void ExposeData() { Scribe_References.Look(ref faction, "faction"); Scribe_Collections.Look(ref ticks, "ticks", LookMode.Value); ticks ??= new(); }
}

public sealed class IdeologyTreeData : MapComponent
{
    public List<FactionTreeHistory> histories = new();
    public bool migrated;
    public IdeologyTreeData(Map map) : base(map) { }
    public void Migrate()
    {
        if (migrated) return;
        migrated = true;
        // Old data has no actor attribution. Preserve it on its original map,
        // under the owning colony; never copy it into other colonies or maps.
        if (ColonyScope.IsColony(map.ParentFaction) && map.treeDestructionTracker != null)
            histories.Add(new FactionTreeHistory { faction = map.ParentFaction, ticks = new List<int>(map.treeDestructionTracker.playerTreeDestructionTicks) });
    }
    public void Record(Faction faction, int tick)
    {
        Migrate();
        var history = histories.Find(h => h.faction == faction);
        if (history == null) { history = new FactionTreeHistory { faction = faction }; histories.Add(history); }
        history.ticks.Add(tick);
    }
    public int Count(Faction faction, int now)
    {
        if (!ColonyScope.IsColony(faction) || (ColonyScope.IsColony(map.ParentFaction) && map.ParentFaction != faction)) return 0;
        if (!migrated)
            return map.ParentFaction == faction ? RecentCount(map.treeDestructionTracker.playerTreeDestructionTicks, now) : 0;
        var history = histories.Find(h => h.faction == faction);
        return history == null ? 0 : RecentCount(history.ticks, now);
    }
    private static int RecentCount(List<int> ticks, int now)
    { int count = 0; foreach (int tick in ticks) if ((long)now - tick < 900000) count++; return count; }
    public override void MapComponentTick()
    {
        if (!ColonyScope.Enabled) return;
        Migrate();
        if (Find.TickManager.TicksGame % 2500 == 0)
            foreach (var history in histories) history.ticks.RemoveAll(t => (long)Find.TickManager.TicksGame - t >= 900000);
    }
    public override void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving) histories.Sort((a,b) => a.faction.loadID.CompareTo(b.faction.loadID));
        Scribe_Values.Look(ref migrated, "ideologyTreesMigrated");
        Scribe_Collections.Look(ref histories, "ideologyFactionTreeHistory", LookMode.Deep);
        histories ??= new();
        if (Scribe.mode == LoadSaveMode.PostLoadInit) histories.RemoveAll(h => h.faction == null);
    }
}

[HarmonyPatch(typeof(TreeDestructionTracker), nameof(TreeDestructionTracker.Notify_TreeCut))]
static class RecordTreeCut
{
    static bool Prefix(TreeDestructionTracker __instance, Pawn by) => Record(__instance, by?.Faction);
    internal static bool Record(TreeDestructionTracker tracker, Faction faction)
    {
        if (!ColonyScope.Enabled || !ColonyScope.IsColony(faction)) return true;
        var data = tracker.map?.GetComponent<IdeologyTreeData>();
        if (data == null) return true;
        data.Record(faction, Find.TickManager.TicksGame); return false;
    }
}
[HarmonyPatch(typeof(TreeDestructionTracker), nameof(TreeDestructionTracker.Notify_TreeDestroyed))]
static class RecordTreeDestroyed
{
    static bool Prefix(TreeDestructionTracker __instance, DamageInfo dInfo) => RecordTreeCut.Record(__instance, dInfo.Instigator?.Faction);
}

[HarmonyPatch(typeof(TreeDestructionTracker), nameof(TreeDestructionTracker.PlayerResponsibleTreeDestructionCount), MethodType.Getter)]
static class FactionTreeCounter
{
    static bool Prefix(TreeDestructionTracker __instance, ref int __result)
    {
        if (!ColonyScope.Enabled || !ColonyScope.IsColony(Faction.OfPlayer)) return true;
        var data = __instance.map?.GetComponent<IdeologyTreeData>();
        if (data == null) return true;
        __result = data.Count(Faction.OfPlayer, Find.TickManager.TicksGame); return false;
    }
}

[HarmonyPatch(typeof(ThoughtWorker_Precept_TreeDensity), "ThoughtStageIndex")]
static class ColonyTreeDensity
{
    private static readonly int[] thresholds = { 2, 5, 8, 10, 13, 16, 20 };
    static bool Prefix(Pawn p, ref int __result)
    {
        if (!ColonyScope.Enabled || !ColonyScope.IsColony(p?.Faction)) return true;
        var map = p.MapHeld;
        int count = map?.GetComponent<IdeologyTreeData>()?.Count(p.Faction, Find.TickManager.TicksGame) ?? 0;
        __result = 0;
        foreach (int threshold in thresholds) if (count >= threshold) __result++; else break;
        return false;
    }
}

[HarmonyPatch(typeof(PreceptComp_KnowsMemoryThought), nameof(PreceptComp_KnowsMemoryThought.Notify_MemberWitnessedAction))]
static class ScopeTreeMemories
{
    static bool Prefix(PreceptComp_KnowsMemoryThought __instance, HistoryEvent ev, Pawn member)
    {
        if (!ColonyScope.Enabled || !ColonyScope.IsColony(member?.Faction)) return true;
        if (typeof(Thought_MemorySocial).IsAssignableFrom(__instance.thought?.thoughtClass)) return true;
        if (ev.def == HistoryEventDefOf.CutTree && ev.args.TryGetArg(HistoryEventArgsNames.Doer, out Pawn by))
            return by.Faction == member.Faction && (by.MapHeld == null || by.MapHeld.ParentFaction == member.Faction || !ColonyScope.IsColony(by.MapHeld.ParentFaction));
        if (ev.def == HistoryEventDefOf.MinifiedTreeDied)
        {
            if (TreeDeathContext.owner != null) return TreeDeathContext.owner == member.Faction;
            if (ev.args.TryGetArg(HistoryEventArgsNames.Victim, out Thing tree) && tree.MapHeld != null)
                return tree.MapHeld.ParentFaction == member.Faction;
            // Unknown origin: keep existing behavior rather than invent an owner.
        }
        return true;
    }
}

// Destroy records the event after despawning. Capture ownership beforehand and
// restore nested calls and exceptions; never infer it from the viewer's map.
[HarmonyPatch(typeof(MinifiedTree), nameof(MinifiedTree.Destroy))]
static class TreeDeathContext
{
    [ThreadStatic] internal static Faction owner;
    static void Prefix(MinifiedTree __instance, out Faction __state)
    {
        __state = owner;
        if (ColonyScope.Enabled)
            owner = __instance.MapHeld?.ParentFaction ?? ThingOwnerUtility.GetAnyParent<Caravan>(__instance)?.Faction;
    }
    static Exception Finalizer(Exception __exception, Faction __state) { owner = __state; return __exception; }
}
