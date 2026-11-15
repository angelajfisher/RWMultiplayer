using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MenuMultiplayerFix;

[StaticConstructorOnStartup]
public static class WallTelevisionBootstrap
{
    public const string HarmonyId = "simone.menumultiplayerfix.walltelevision";
    static WallTelevisionBootstrap() => Install();
    public static void EnsureLoaded() { }
    private static void Install()
    {
        var harmony = new Harmony(HarmonyId);
        try
        {
            harmony.PatchAll(typeof(WallTelevisionBootstrap).Assembly);
            Log.Message("[MenuMultiplayerFix] Wall TV fix 1.5.1 loaded: LTS wall televisions use reachable viewing positions.");
        }
        catch (Exception e)
        {
            harmony.UnpatchAll(HarmonyId);
            Log.Error($"[MenuMultiplayerFix] Wall TV patch failed and was rolled back: {e}");
            throw;
        }
    }
}

public static class WallTelevisionJobs
{
    public const string WallTvDef = "LTS_WallTelevision";
    private static readonly MethodInfo CanInteract = AccessTools.Method(typeof(JoyGiver_WatchBuilding), "CanInteractWith");

    // Small engine-query methods also let the regression fixture control map state.
    public static IEnumerable<Thing> Candidates(JoyGiver_WatchBuilding giver, Pawn pawn)
    {
        if (giver.def?.thingDefs == null) yield break;
        foreach (var def in giver.def.thingDefs)
            if (def.defName == WallTvDef)
                foreach (var thing in pawn.Map.listerThings.ThingsOfDef(def)) yield return thing;
    }
    public static bool BlockedTile(Pawn pawn, Thing tv) => !pawn.Map.pathing.For(pawn).pathGrid.Walkable(tv.Position);
    public static bool ReachableSeat(Pawn pawn, IntVec3 cell) => pawn.Map.reachability.CanReach(pawn.Position, cell, PathEndMode.OnCell, TraverseParms.For(pawn));
    public static Building_Bed BedFor(Pawn pawn) => pawn.CurrentBed();

    public static Job TryGive(JoyGiver_InteractBuilding giver, Pawn pawn, bool inBed)
    {
        // Do not override other mods' subclasses or unrelated recreation givers.
        if (giver?.GetType() != typeof(JoyGiver_WatchBuilding) || pawn?.Spawned != true) return null;
        Building_Bed bed = inBed ? BedFor(pawn) : null;
        if (inBed && bed == null) return null;
        var candidates = new List<Thing>();
        foreach (var thing in Candidates((JoyGiver_WatchBuilding)giver, pawn))
            if (thing?.def?.defName == WallTvDef && thing.Spawned && thing.Map == pawn.Map) candidates.Add(thing);
        // Distance + stable ID avoids introducing client-dependent choice or RNG.
        candidates.Sort((a, b) =>
        {
            int distance = a.Position.DistanceToSquared(pawn.Position).CompareTo(b.Position.DistanceToSquared(pawn.Position));
            return distance != 0 ? distance : a.thingIDNumber.CompareTo(b.thingIDNumber);
        });
        foreach (var tv in candidates)
        {
            if (!BlockedTile(pawn, tv)) continue;
            // Retain vanilla reservation, power, forbidding, political/social
            // propriety and in-bed visibility checks before constructing a job.
            if (!(bool)CanInteract.Invoke(giver, new object[] { pawn, tv, inBed })) continue;
            if (inBed) return JobMaker.MakeJob(giver.def.jobDef, tv, pawn.Position, bed);
            if (!WatchBuildingUtility.TryFindBestWatchCell(tv, pawn, giver.def.desireSit, out var cell, out var chair)) continue;
            if (!ReachableSeat(pawn, cell)) continue;
            return JobMaker.MakeJob(giver.def.jobDef, tv, cell, chair);
        }
        return null;
    }
}

[HarmonyPatch]
static class WallTelevisionRecreationPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { nameof(JoyGiver_InteractBuilding.TryGiveJob), nameof(JoyGiver_InteractBuilding.TryGiveJobWhileInBed) })
            yield return AccessTools.DeclaredMethod(typeof(JoyGiver_InteractBuilding), name, new[] { typeof(Pawn) }) ??
                throw new MissingMethodException("TV recreation method changed: " + name);
    }
    static void Postfix(JoyGiver_InteractBuilding __instance, Pawn pawn, MethodBase __originalMethod, ref Job __result)
    {
        if (__result == null)
            __result = WallTelevisionJobs.TryGive(__instance, pawn, __originalMethod.Name == nameof(JoyGiver_InteractBuilding.TryGiveJobWhileInBed));
    }
}
