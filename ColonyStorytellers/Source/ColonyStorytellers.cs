using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Client.Comp;
using RimWorld;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

namespace ColonyStorytellers;

public sealed class ColonyStorytellersMod : Mod
{
    public ColonyStorytellersMod(ModContentPack content) : base(content)
    {
        ApplyPatches();
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            int enabled = StorytellerAccess.Enable();
            Log.Message($"[Colony Storytellers] v0.1.0: enabled {enabled}/4 existing storyteller controls for colony players.");
        });
    }
    public static void ApplyPatches() => new Harmony("angelajfisher.colonystorytellers").PatchAll(typeof(ColonyStorytellersMod).Assembly);
}

public static class StorytellerAccess
{
    public static bool IsStorytellerField(object handler) =>
        ReferenceEquals(handler, SyncFields.SyncStorytellerDef) ||
        ReferenceEquals(handler, SyncFields.SyncStorytellerDifficultyDef) ||
        ReferenceEquals(handler, SyncFields.SyncStorytellerDifficulty);
    public static bool IsDifficultyCopy(MethodInfo method) => method?.DeclaringType == typeof(Difficulty) && method.Name == nameof(Difficulty.CopyFrom);

    public static int Enable()
    {
        int count = 0;
        foreach (var field in new[] { SyncFields.SyncStorytellerDef, SyncFields.SyncStorytellerDifficultyDef, SyncFields.SyncStorytellerDifficulty })
            if (field is SyncHandler handler) { handler.hostOnly = false; count++; }
        foreach (var handler in Sync.handlers)
            if (handler is SyncMethod method && IsDifficultyCopy(method.method)) { handler.hostOnly = false; count++; }
        return count;
    }

    public static bool MayApply()
    {
        if (MP.Client == null) return true;
        var faction = Faction.OfPlayer;
        return faction?.def?.isPlayer == true && faction != MP.game?.worldComp?.spectatorFaction;
    }
}

[HarmonyPatch(typeof(Sync), nameof(Sync.PostInitHandlers))]
static class EnableBeforeHandshake
{
    static void Postfix() => StorytellerAccess.Enable();
}

[HarmonyPatch(typeof(SyncField), nameof(SyncField.Handle))]
static class RejectSpectatorStorytellerField
{
    static bool Prefix(SyncField __instance) => !StorytellerAccess.IsStorytellerField(__instance) || StorytellerAccess.MayApply();
}

[HarmonyPatch(typeof(SyncMethod), nameof(SyncMethod.Handle))]
static class RejectSpectatorDifficultyCopy
{
    static bool Prefix(SyncMethod __instance) => !StorytellerAccess.IsDifficultyCopy(__instance.method) || StorytellerAccess.MayApply();
}

public static class DifficultyIsolation
{
    public static Difficulty Clone(Difficulty source)
    {
        if (source == null) return null;
        // All saved settings are scalar values or immutable Def references. Rebuild
        // the mutable private curve so the two colonies share no cached curve.
        var copy = Gen.MemberwiseClone(source);
        AccessTools.Method(typeof(Difficulty), "SetMinThreatPointsCurve").Invoke(copy, null);
        return copy;
    }

    public static int Repair()
    {
        if (MP.Client == null || MP.game?.gameComp?.multifaction != true || MP.game.worldComp?.factionData == null) return 0;
        int repaired = 0;
        var seen = new HashSet<Difficulty>();
        foreach (var data in MP.game.worldComp.factionData.Values)
        {
            var storyteller = data.storyteller;
            if (storyteller?.difficulty != null && !seen.Add(storyteller.difficulty))
            {
                storyteller.difficulty = Clone(storyteller.difficulty);
                seen.Add(storyteller.difficulty); repaired++;
            }
        }
        return repaired;
    }
}

[HarmonyPatch(typeof(FactionWorldData), nameof(FactionWorldData.New))]
static class NewColonyDifficulty
{
    static void Postfix(FactionWorldData __result)
    {
        if (__result?.storyteller?.difficulty != null)
            __result.storyteller.difficulty = DifficultyIsolation.Clone(__result.storyteller.difficulty);
    }
}

public sealed class ColonyStorytellerRepair : GameComponent
{
    bool checkedData;
    public ColonyStorytellerRepair(Game game) { }
    public override void FinalizeInit() => TryRepair();
    public override void GameComponentTick() { if (!checkedData) TryRepair(); }
    void TryRepair()
    {
        if (MP.Client == null || MP.game?.worldComp?.factionData == null) return;
        checkedData = true;
        int repaired = DifficultyIsolation.Repair();
        if (repaired > 0) Log.Message($"[Colony Storytellers] Isolated {repaired} shared colony difficulty settings.");
    }
}
