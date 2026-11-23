using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client;
using RimWorld;
using Verse;

namespace IdeologyFactionFix;

[HarmonyPatch]
static class SingleRoleReadsAndRefresh
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "get_ChosenPawnValue", "get_AbilitiesForReading", "ChosenPawnSingle", "RecacheActivity", "FillOrUpdateAbilities" })
            yield return AccessTools.Method(typeof(Precept_RoleSingle), name);
    }
    static void Prefix(Precept_RoleSingle __instance, MethodBase __originalMethod, out RoleScope __state) =>
        __state = RoleScope.Enter(__instance, Faction.OfPlayer, __originalMethod.Name == "RecacheActivity" || __originalMethod.Name == "FillOrUpdateAbilities");
    static Exception Finalizer(Exception __exception, RoleScope __state) { __state?.Dispose(); return __exception; }
}

[HarmonyPatch]
static class SingleRolePawnOperations
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "Assign", "Unassign", "IsAssigned", "AbilitiesFor" }) yield return AccessTools.Method(typeof(Precept_RoleSingle), name);
    }
    static void Prefix(Precept_RoleSingle __instance, Pawn __0, MethodBase __originalMethod, out RoleScope __state) =>
        __state = RoleScope.Enter(__instance, ColonyScope.ForPawn(__0) ?? Faction.OfPlayer, __originalMethod.Name == "Assign" || __originalMethod.Name == "Unassign");
    static Exception Finalizer(Exception __exception, RoleScope __state) { __state?.Dispose(); return __exception; }
}

[HarmonyPatch(typeof(Precept_RoleSingle), nameof(Precept_RoleSingle.Notify_MemberChangedFaction))]
static class RoleFactionChange
{
    static void Prefix(Precept_RoleSingle __instance, Faction oldFaction, out RoleScope __state) => __state = RoleScope.Enter(__instance, oldFaction, true);
    static Exception Finalizer(Exception __exception, RoleScope __state) { __state?.Dispose(); return __exception; }
}

// Immutable snapshots avoid escaping the borrowed fields through an iterator.
// Empty and unchanged rosters allocate nothing in the steady state.
[HarmonyPatch(typeof(Precept_RoleSingle), nameof(Precept_RoleSingle.ChosenPawns))]
static class ScopedRolePawns
{
    static bool Prefix(Precept_RoleSingle __instance, ref IEnumerable<Pawn> __result)
    {
        if (!ColonyScope.Enabled || IdeologyColonyData.Current == null) return true;
        if (AllRoleHolders.depth > 0 && IdeologyColonyData.Current.Has(__instance)) { __result = IdeologyColonyData.Current.AllHolders(__instance).ToArray(); return false; }
        if (!ColonyScope.IsColony(Faction.OfPlayer)) { __result = Array.Empty<Pawn>(); return false; }
        __result = IdeologyColonyData.Current.Read(__instance,Faction.OfPlayer).ChosenPawns();
        return false;
    }
}

[HarmonyPatch(typeof(Precept_Role), nameof(Precept_Role.Active), MethodType.Getter)]
static class FactionRoleActive
{
    static bool Prefix(Precept_Role __instance, ref bool __result)
    {
        if (!ColonyScope.Enabled || __instance is not Precept_RoleSingle single || RoleScope.IsCurrent(single)) return true;
        if (!ColonyScope.IsColony(Faction.OfPlayer)) { __result = false; return false; }
        var data = IdeologyColonyData.Current;
        if (data == null) return true;
        __result = data.Read(single, Faction.OfPlayer).active || DebugSettings.activateAllIdeoRoles;
        return false;
    }
}

// Refresh every colony, never whichever player happened to view the ideology.
[HarmonyPatch(typeof(Precept_RoleSingle), nameof(Precept_RoleSingle.RecacheActivity))]
static class RefreshAllFactions
{
    [HarmonyPriority(Priority.First)]
    static bool Prefix(Precept_RoleSingle __instance)
    {
        if (!ColonyScope.Enabled || RoleScope.IsCurrent(__instance) || IdeologyColonyData.Current == null) return true;
        var factions = Find.FactionManager.AllFactionsListForReading;
        var data = IdeologyColonyData.Current;
        for (int i = 0; i < factions.Count; i++)
        {
            var faction = factions[i];
            if (!ColonyScope.IsColony(faction)) continue;
            if (faction.ideos?.Has(__instance.ideo) != true && !data.Has(__instance,faction) && __instance.chosenPawn?.pawn?.Faction != faction) continue;
            using (RoleScope.Enter(__instance, faction, true)) __instance.RecacheActivity();
        }
        return false;
    }
}

// Pawn-facing checks must use that pawn's faction, even on another colony's map.
[HarmonyPatch]
static class PawnRoleOperations
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "SituationThoughtsToAdd", "RequirementsMet", "GetFirstUnmetRequirement" })
            yield return AccessTools.Method(typeof(Precept_Role), name);
    }
    struct State { internal RoleScope role; internal bool faction; }
    static void Prefix(Precept_Role __instance, Pawn __0, out State __state)
    {
        __state = default;
        if (!ColonyScope.Enabled) return;
        var faction = ColonyScope.ForPawn(__0);
        if (__instance is Precept_RoleSingle single) __state.role = RoleScope.Enter(single, faction, false);
        else if (faction != null && faction != Faction.OfPlayer) { FactionContext.Push(faction); __state.faction = true; }
    }
    static Exception Finalizer(Exception __exception, State __state)
    { __state.role?.Dispose(); if (__state.faction) FactionContext.Pop(); return __exception; }
}

// Save independent holders only once. The vanilla slot is retained for old-save
// migration until faction state exists; then an empty compatibility slot is saved.
[HarmonyPatch(typeof(Precept_RoleSingle), nameof(Precept_RoleSingle.ExposeData))]
static class SaveRoleCompatibilitySlot
{
    static void Prefix(Precept_RoleSingle __instance, out IdeoRoleInstance __state)
    {
        __state = null;
        if (Scribe.mode == LoadSaveMode.Saving && IdeologyColonyData.Current?.Has(__instance) == true)
        {
            __state = __instance.chosenPawn; __instance.chosenPawn = new IdeoRoleInstance(__instance);
        }
    }
    static Exception Finalizer(Exception __exception, Precept_RoleSingle __instance, IdeoRoleInstance __state)
    { if (__state != null) __instance.chosenPawn = __state; return __exception; }
}

// Multi-holder roles already store independent instances. Their capacity and
// empty-role checks must enumerate only the current colony's members.
[HarmonyPatch(typeof(Precept_RoleMulti), nameof(Precept_RoleMulti.ChosenPawns))]
static class MultiRolePawns
{
    static bool Prefix(Precept_RoleMulti __instance, ref IEnumerable<Pawn> __result)
    {
        if (!ColonyScope.Enabled || AllRoleHolders.depth > 0) return true;
        __result = ColonyScope.IsColony(Faction.OfPlayer) ? ForFaction(__instance.chosenPawns,Faction.OfPlayer) : Array.Empty<Pawn>();
        return false;
    }
    static IEnumerable<Pawn> ForFaction(List<IdeoRoleInstance> instances, Faction faction)
    {
        for (int i = 0; i < instances.Count; i++) if (instances[i].pawn?.Faction == faction) yield return instances[i].pawn;
    }
}

// Removing a shared belief/role is a definition change affecting every colony.
// Its cleanup must notify every holder before independent state is discarded.
[HarmonyPatch]
static class AllRoleHolders
{
    [ThreadStatic] internal static int depth;
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Ideo),nameof(Ideo.RemovePrecept));
        yield return AccessTools.Method(typeof(Precept_Role),nameof(Precept_Role.Notify_RemovedByReforming));
    }
    static void Prefix(out bool __state) { __state = ColonyScope.Enabled; if (__state) depth++; }
    static Exception Finalizer(Exception __exception, bool __state)
    {
        if (__state) { depth--; if (depth == 0) IdeologyColonyData.Current?.DiscardRemovedRoles(); }
        return __exception;
    }
}
