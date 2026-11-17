using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Common;

static class Program
{
    static int checks;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
    static void Patch(Harmony harmony, MethodBase method)
    {
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(RenderProfiler), "Begin"), finalizer: new HarmonyMethod(typeof(RenderProfiler), "End"));
    }
    static void Main()
    {
        Console.WriteLine("Architecture=" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        var type = typeof(RenderProfiler);
        var roots = ((ValueTuple<Type, string>[])AccessTools.Field(type, "roots").GetValue(null));
        foreach (var root in roots)
            Check(Array.Exists(root.Item1.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => m.Name == root.Item2), "installed game stage exists: " + root.Item1.FullName + "." + root.Item2);
        var session = new RenderTimingSession(2);
        AccessTools.Field(type, "session").SetValue(null, session);
        AccessTools.Field(type, "mainThread").SetValue(null, Thread.CurrentThread.ManagedThreadId);
        var indices = (Dictionary<MethodBase, int>)AccessTools.Field(type, "indices").GetValue(null);
        var outer = AccessTools.Method(typeof(Program), nameof(Outer)); var inner = AccessTools.Method(typeof(Program), nameof(Inner));
        indices.Add(outer, 0); indices.Add(inner, 1);
        var h = new Harmony("render-profile.smoke"); Patch(h, outer); Patch(h, inner);
        Check(Outer(4, false) == 6 && session.Snapshot()[0].Calls == 0, "disabled instrumentation preserves return values and records nothing");
        session.Start();
        Check(Outer(4, false) == 6, "enabled instrumentation preserves return values");
        var stats = session.Snapshot();
        Check(stats[0].Calls == 1 && stats[1].Calls == 1, "actual Harmony prefix/finalizer records nested calls");
        Check(stats[0].Inclusive >= stats[1].Inclusive && stats[0].Exclusive + stats[1].Inclusive == stats[0].Inclusive, "nested elapsed and exclusive timing agree under game Mono");
        bool caught = false;
        try { Outer(4, true); } catch (InvalidOperationException e) { caught = e.Message == "original exception"; }
        Check(caught, "profiling finalizer preserves the game's original exception");
        Check(session.Snapshot()[0].Calls == 2 && session.Snapshot()[1].Calls == 2 && session.Dropped == 0, "throwing nested calls unwind timing scopes without leaks");
        Exception threadFailure = null;
        var worker = new Thread(() => { try { Outer(1, false); } catch (Exception e) { threadFailure = e; } });
        worker.Start(); worker.Join();
        Check(threadFailure == null && session.Snapshot()[0].Calls == 2, "worker-thread drawing is excluded safely from main-thread capture");
        session.Stop(); Outer(2, false);
        Check(session.Snapshot()[0].Calls == 2, "stopping recording prevents further measurements");
        var component = (AsyncTimeComp)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(AsyncTimeComp));
        var map = (Verse.Map)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.Map));
        AccessTools.Field(typeof(Verse.Map), "uniqueID").SetValue(map, 44); component.map = map; component.mapTicks = 100;
        var captureType = type.GetNestedType("MapCapture", BindingFlags.NonPublic);
        var capture = Activator.CreateInstance(captureType, true);
        AccessTools.Field(captureType, "component").SetValue(capture, component);
        var captures = (System.Collections.IDictionary)AccessTools.Field(type, "mapCaptures").GetValue(null); captures.Add(44, capture);
        var mapTick = AccessTools.Method(typeof(AsyncTimeComp), nameof(AsyncTimeComp.Tick));
        h.Patch(mapTick, prefix: new HarmonyMethod(type, "BeginMap") { priority = Priority.First }, finalizer: new HarmonyMethod(type, "EndMap"));
        h.Patch(mapTick, prefix: new HarmonyMethod(typeof(Program), nameof(MapBody)) { priority = Priority.Last });
        session.Start(); component.Tick();
        var counter = (TickTimingCounter)AccessTools.Field(captureType, "timing").GetValue(capture);
        Check(counter.Calls == 1 && counter.AdvancedTicks == 1 && component.mapTicks == 101, "actual map tick hook measures advances without changing the controlled body");
        failMap = true; caught = false;
        try { component.Tick(); } catch (InvalidOperationException) { caught = true; }
        Check(caught && counter.Exceptions == 1 && counter.AdvancedTicks == 2, "map timing preserves exceptions and records partial advancement explicitly");
        session.Stop(); failMap = false; component.Tick();
        Check(counter.Calls == 2, "disabled map timing does not collect data");
        captures.Clear();
        h.UnpatchAll(h.Id);
        Console.WriteLine("Render profiler smoke: " + checks + " checks passed");
    }
    static bool failMap;
    static bool MapBody(AsyncTimeComp __instance) { __instance.mapTicks++; if (failMap) throw new InvalidOperationException("map fixture"); return false; }
    [MethodImpl(MethodImplOptions.NoInlining)] static int Outer(int input, bool fail) => Inner(input, fail) + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] static int Inner(int input, bool fail)
    {
        if (fail) throw new InvalidOperationException("original exception");
        Thread.SpinWait(10000); return input + 1;
    }
}
