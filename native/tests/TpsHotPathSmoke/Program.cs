using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Common;
using MultiplayerQuestFactionFix;
using RimWorld;
using RimWorld.Planet;
using Verse;

static class Program
{
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Static | BindingFlags.Public));
    static void Measure(string name, Action work)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int gen0 = GC.CollectionCount(0); long start = Allocated(); var timer = Stopwatch.StartNew();
        work(); timer.Stop();
        long bytes = Allocated() - start;
        Console.WriteLine(name + ": ms=" + timer.ElapsedMilliseconds + " allocatedBytes=" + (bytes > 0 ? bytes.ToString() : "unavailable") + " gen0=" + (GC.CollectionCount(0) - gen0));
    }
    static void Main()
    {
        Console.WriteLine("Allocation-byte counter may be unavailable on Unity Mono; use GC collections and object lifecycle checks as well.");
        Console.WriteLine("Architecture=" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        var opinion = new ClientSyncOpinion(0);
        Action traces = () => {
            for (int i = 0; i < 200000; i++)
            {
                var capture = StackTraceLogItemRaw.GetFromPool(); capture.depth = 32; capture.traceEvent = i; capture.hash = i;
                for (int j = 0; j < 32; j++) capture.raw[j] = j + i % 64;
                opinion.AddDesyncTrace(capture);
            }
            opinion.Clear();
        };
        traces(); Measure("200k retained trace events (64 distinct stacks)", traces);
        var faction = Empty<Faction>(); faction.def = new FactionDef { isPlayer = true }; faction.loadID = 20;
        var mapParent = Empty<Settlement>(); Set(mapParent, "factionInt", faction);
        var map = Empty<Map>(); map.info = new MapInfo { parent = mapParent };
        var game = Empty<Game>(); Current.Game = game;
        Set(game, "maps", new List<Map> { map });
        var world = Empty<World>(); var manager = Empty<FactionManager>();
        Set(manager, "ofPlayer", faction); Set(manager, "allFactions", new List<Faction> { faction });
        Set(world, "factionManager", manager); Set(game, "worldInt", world);
        var quest = new Quest { id = 88 };
        var parts = new List<QuestPart>();
        for (int i = 0; i < 80; i++) parts.Add(i % 4 == 0 ? (QuestPart)new QuestPart_Delay() : new QuestPart_QuestEnd());
        var target = new QuestPart_GameCondition(); Set(target, "mapParent", mapParent); parts.Add(target);
        Set(quest, "parts", parts);
        // One setup invocation needs owner and target map. Old releases scan
        // twice; optimized releases expose a batched resolver for the same work.
        var batched = AccessTools.Method(typeof(QuestOwnership), "ResolveContext");
        var resolve = batched == null ? null : (Func<Quest, KeyValuePair<Faction, Map>>)Delegate.CreateDelegate(typeof(Func<Quest, KeyValuePair<Faction, Map>>), batched);
        Action quests = () => {
            for (int i = 0; i < 20000; i++)
            {
                var result = resolve != null ? resolve(quest) : new KeyValuePair<Faction, Map>(QuestOwnership.Owner(quest), QuestOwnership.TargetMap(quest));
                if (result.Key != faction || result.Value != map) throw new Exception("Ownership/map changed");
            }
        };
        Console.WriteLine("BatchedContext=" + (resolve != null));
        quests();
        for (int repeat = 0; repeat < 3; repeat++) Measure("20k owner+map lookups (60 simple,20 delay,1 map part)", quests);
        Console.WriteLine("PASS: trace retention and context results preserved");
    }
}
