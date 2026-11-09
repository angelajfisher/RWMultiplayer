using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace DryadHealingFix;

public sealed class DryadHealingFixMod : Mod
{
    public DryadHealingFixMod(ModContentPack content) : base(content)
    {
        ApplyPatches();
        Log.Message("[Dryad Healing Fix] v0.1.0: healing pods enabled by default; choices survive mind-state resets.");
    }
    public static void ApplyPatches() => new Harmony("angelajfisher.dryadhealingfix").PatchAll(typeof(DryadHealingFixMod).Assembly);
}

public static class HealingPreference
{
    sealed class Migration { public bool applied; }
    static readonly ConditionalWeakTable<Pawn_MindState, Migration> migrations = new();
    public static bool IsDryad(Pawn_MindState state) => state.pawn?.def?.race?.Dryad == true;
    public static void Initialize(Pawn_MindState state)
    {
        if (IsDryad(state)) state.returnToHealingPod = true;
    }
    public static void Expose(Pawn_MindState state)
    {
        if (!IsDryad(state)) return;
        var migration = migrations.GetOrCreateValue(state);
        if (Scribe.mode == LoadSaveMode.Saving) migration.applied = true;
        Scribe_Values.Look(ref migration.applied, "dryadHealingDefaultApplied", false);
        if (Scribe.mode == LoadSaveMode.PostLoadInit && !migration.applied)
        {
            state.returnToHealingPod = true;
            migration.applied = true;
        }
    }
}

[HarmonyPatch(typeof(Pawn_MindState), MethodType.Constructor, new[] { typeof(Pawn) })]
static class DefaultHealingPatch
{
    static void Postfix(Pawn_MindState __instance) => HealingPreference.Initialize(__instance);
}

[HarmonyPatch(typeof(Pawn_MindState), nameof(Pawn_MindState.Reset), new[] { typeof(bool), typeof(bool), typeof(bool) })]
static class PreserveHealingPatch
{
    static void Prefix(Pawn_MindState __instance, out bool __state) => __state = __instance.returnToHealingPod;
    static void Postfix(Pawn_MindState __instance, bool __state)
    {
        if (HealingPreference.IsDryad(__instance)) __instance.returnToHealingPod = __state;
    }
}

[HarmonyPatch(typeof(Pawn_MindState), nameof(Pawn_MindState.ExposeData))]
static class PersistHealingPatch
{
    static void Postfix(Pawn_MindState __instance) => HealingPreference.Expose(__instance);
}
