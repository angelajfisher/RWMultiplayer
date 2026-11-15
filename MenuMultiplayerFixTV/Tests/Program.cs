using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using MenuMultiplayerFix;
using RimWorld;
using Verse;
using Verse.AI;

static class Program
{
    static int checks, candidateCalls, vanillaCalls;
    static bool blocked = true, allowed = true, seat = true, reachable = true;
    static Thing vanillaResult;
    static List<Thing> candidates = new();
    static Building chair;
    static Building_Bed bed;
    static readonly IntVec3 viewCell = new(12, 0, 10);
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
    static Building TV(int id, int x, int map = 0, string defName = WallTelevisionJobs.WallTvDef)
    {
        var tv = Empty<Building>(); tv.def = Empty<ThingDef>(); tv.def.defName = defName; tv.thingIDNumber = id;
        Set(tv, "positionInt", new IntVec3(x, 0, 10)); Set(tv, "mapIndexOrState", (sbyte)map); return tv;
    }
    static int Main()
    {
        var log = new Harmony("wall-tv.smoke.log");
        log.Patch(AccessTools.Method(typeof(Log), nameof(Log.Message), new[] { typeof(string) }), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(PrintLog))));
        Current.Game = Empty<Game>(); Set(Current.Game, "maps", new List<Map> { Empty<Map>(), Empty<Map>() });
        Set(Current.Game, "uniqueIDsManager", new UniqueIDsManager()); Set(Current.Game, "tickManager", Empty<TickManager>());
        var pawn = Empty<Pawn>(); Set(pawn, "positionInt", new IntVec3(10, 0, 10));
        Set(pawn, "mapIndexOrState", (sbyte)0);
        var giver = new JoyGiver_WatchBuilding { def = new JoyGiverDef { jobDef = new JobDef { defName = "WatchTelevision" }, desireSit = true } };
        chair = Empty<Building>(); bed = Empty<Building_Bed>();
        var wall = TV(20, 15); candidates.Add(wall);
        var fixture = new Harmony("wall-tv.smoke.engine");
        Patch(fixture, typeof(JoyGiver_InteractBuilding), "FindBestGame", nameof(FindBest));
        Patch(fixture, typeof(WallTelevisionJobs), nameof(WallTelevisionJobs.Candidates), nameof(Candidates));
        Patch(fixture, typeof(WallTelevisionJobs), nameof(WallTelevisionJobs.BlockedTile), nameof(Blocked));
        Patch(fixture, typeof(WallTelevisionJobs), nameof(WallTelevisionJobs.ReachableSeat), nameof(Reachable));
        Patch(fixture, typeof(WallTelevisionJobs), nameof(WallTelevisionJobs.BedFor), nameof(Bed));
        Patch(fixture, typeof(JoyGiver_WatchBuilding), "CanInteractWith", nameof(CanInteract));
        Patch(fixture, typeof(WatchBuildingUtility), nameof(WatchBuildingUtility.TryFindBestWatchCell), nameof(WatchCell));
        Check(giver.TryGiveJob(pawn) == null, "baseline vanilla has no job when target wall tile is unreachable");
        WallTelevisionBootstrap.EnsureLoaded();
        var job = giver.TryGiveJob(pawn);
        Check(job != null && job.targetA.Thing == wall && job.targetB.Cell == viewCell && job.targetC.Thing == chair, "patched actual TV giver returns a job for the viewing seat, not the wall");
        Check(vanillaCalls > 0, "vanilla interaction eligibility is consulted");
        allowed = false;
        Check(giver.TryGiveJob(pawn) == null, "power/reservation/forbidding/social checks cannot be bypassed when vanilla rejects interaction");
        allowed = true; seat = false;
        Check(giver.TryGiveJob(pawn) == null, "TV without an eligible viewing cell/chair produces no job");
        seat = true; reachable = false;
        Check(giver.TryGiveJob(pawn) == null, "unreachable viewing seat produces no job");
        reachable = true; blocked = false;
        Check(giver.TryGiveJob(pawn) == null, "walkable TV tiles do not use the wall-only fallback");
        blocked = true;
        var originalTv = TV(25, 14, defName: "FlatscreenTelevision"); vanillaResult = originalTv; candidateCalls = 0;
        job = giver.TryGiveJob(pawn);
        Check(job != null && job.targetA.Thing == originalTv && candidateCalls == 0, "successful vanilla TV jobs remain untouched");
        vanillaResult = null;
        candidates = new List<Thing> { originalTv };
        Check(giver.TryGiveJob(pawn) == null, "ordinary/freestanding TV defs are not patched");
        candidates = new List<Thing> { TV(21, 15, 1) };
        Check(giver.TryGiveJob(pawn) == null, "another map's TV cannot be selected");
        candidates = new List<Thing> { TV(21, 15, -1) };
        Check(giver.TryGiveJob(pawn) == null, "unspawned TVs cannot be selected");
        candidates = new List<Thing> { wall, TV(10, 5) };
        Check(giver.TryGiveJob(pawn).targetA.Thing.thingIDNumber == 10, "equal-distance TVs use stable thing IDs rather than list order or random draws");
        candidates.Reverse();
        Check(giver.TryGiveJob(pawn).targetA.Thing.thingIDNumber == 10, "candidate list order cannot change the selected TV");
        candidates = new List<Thing> { wall };
        job = giver.TryGiveJobWhileInBed(pawn);
        Check(job != null && job.targetA.Thing == wall && job.targetB.Cell == pawn.Position && job.targetC.Thing == bed, "patients retain the vanilla in-bed watch job targets");
        allowed = false;
        Check(giver.TryGiveJobWhileInBed(pawn) == null, "in-bed visibility/interaction rejection remains effective");
        allowed = true; bed = null;
        Check(giver.TryGiveJobWhileInBed(pawn) == null, "no in-bed fallback without a current bed");
        var otherGiver = Empty<JoyGiver_InteractBuildingSitAdjacent>();
        Check(WallTelevisionJobs.TryGive(otherGiver, pawn, false) == null, "other recreation giver types are unchanged");
        Set(pawn, "mapIndexOrState", (sbyte)-1);
        Check(WallTelevisionJobs.TryGive(giver, pawn, false) == null, "unspawned pawns are excluded");
        Console.WriteLine("Wall TV fix: " + checks + " checks passed"); return 0;
    }
    static void Patch(Harmony h, Type type, string original, string prefix) => h.Patch(AccessTools.Method(type, original), prefix: new HarmonyMethod(AccessTools.Method(typeof(Program), prefix)));
    static bool PrintLog(string text) { Console.WriteLine(text); return false; }
    static bool FindBest(ref Thing __result) { __result = vanillaResult; return false; }
    static bool Candidates(ref IEnumerable<Thing> __result) { candidateCalls++; __result = candidates; return false; }
    static bool Blocked(ref bool __result) { __result = blocked; return false; }
    static bool Reachable(ref bool __result) { __result = reachable; return false; }
    static bool Bed(ref Building_Bed __result) { __result = bed; return false; }
    static bool CanInteract(ref bool __result) { vanillaCalls++; __result = allowed; return false; }
    static bool WatchCell(ref bool __result, ref IntVec3 result, ref Building chair) { __result = seat; result = viewCell; chair = Program.chair; return false; }
}
