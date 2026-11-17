using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Debug = UnityEngine.Debug;

namespace Multiplayer.Client;

public static class RenderProfiler
{
    private const string HarmonyId = "rwmt.Multiplayer.render-profile";
    private const int MaxTargets = 160, MaxFrames = 8192;
    private static readonly Dictionary<MethodBase, int> indices = new();
    private static readonly List<string> names = new();
    private static readonly List<string> coverage = new();
    private static readonly float[] frameMs = new float[MaxFrames];
    private static RenderTimingSession session;
    private static long started;
    private static int frames, lastFrame = -1, samples;
    private static int mainThread;
    private static string metadata;
    public static bool Active => session?.Active == true;

    public static void Install()
    {
        if (session != null) return;
        try { InstallCore(); }
        catch (Exception e)
        {
            new Harmony(HarmonyId).UnpatchAll(HarmonyId);
            session = null; indices.Clear(); names.Clear();
            Log.Warning("[Render profiler] Unavailable; optional hooks rolled back: " + e.Message);
        }
    }
    private static void InstallCore()
    {
        mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        var harmony = new Harmony(HarmonyId);
        var roots = new[] {
            ("Verse.DynamicDrawManager", "DrawDynamicThings"),
            ("Verse.MapDrawer", "DrawMapMesh"),
            ("Verse.MapDrawer", "MapMeshDrawerUpdate_First"),
            ("Verse.PawnRenderer", "RenderPawnAt"),
            ("Verse.OverlayDrawer", "DrawAllOverlays"),
            ("Verse.DesignationManager", "DrawDesignations"),
            ("Verse.FleckManager", "Draw"),
            ("Verse.WindowStack", "WindowsOnGUI"),
            ("RimWorld.MainButtonsRoot", "MainButtonsOnGUI"),
            ("Verse.UIRoot_Play", "UIRootUpdate"),
            ("Verse.TickManager", "TickManagerUpdate")
        };
        var targets = new HashSet<MethodBase>();
        foreach (var root in roots)
        {
            var type = AccessTools.TypeByName(root.Item1);
            var found = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == root.Item2 && !m.IsAbstract && !m.ContainsGenericParameters).ToArray();
            if (found?.Length > 0) foreach (var method in found) targets.Add(method);
            else coverage.Add("Unavailable stage: " + root.Item1 + "." + root.Item2);
        }
        // Only actual overrides, never shared empty base implementations.
        foreach (var pack in LoadedModManager.RunningModsListForReading)
        foreach (var asm in pack.assemblies.loadedAssemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            catch { coverage.Add("Could not inspect assembly: " + asm.GetName().Name); continue; }
            foreach (var type in types)
            {
                if (type.IsAbstract || type.ContainsGenericParameters) continue;
                string methodName = typeof(MapComponent).IsAssignableFrom(type) ? "MapComponentDraw" :
                    typeof(GameComponent).IsAssignableFrom(type) ? "GameComponentOnGUI" :
                    typeof(WorldComponent).IsAssignableFrom(type) ? "WorldComponentOnGUI" : null;
                if (methodName == null) continue;
                var method = AccessTools.DeclaredMethod(type, methodName, Type.EmptyTypes);
                if (method != null && !method.IsAbstract) targets.Add(method);
            }
        }
        foreach (var target in targets.OrderBy(m => m.DeclaringType?.FullName).ThenBy(m => m.ToString()))
        {
            if (names.Count >= MaxTargets) { coverage.Add("Target limit reached; omitted: " + target); continue; }
            int index = names.Count;
            try
            {
                indices.Add(target, index);
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(RenderProfiler), nameof(Begin)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(RenderProfiler), nameof(End)) { priority = Priority.Last });
                names.Add(target.DeclaringType.Assembly.GetName().Name + " :: " + target.DeclaringType.FullName + "." + target.Name);
            }
            catch (Exception e)
            {
                harmony.Unpatch(target, HarmonyPatchType.All, HarmonyId);
                indices.Remove(target); coverage.Add("Not instrumented: " + target + " (" + e.GetType().Name + ")");
            }
        }
        session = new RenderTimingSession(Math.Max(1, names.Count));
        var gui = AccessTools.Method(typeof(UIRoot_Play), "UIRootOnGUI");
        if (gui != null) harmony.Patch(gui, prefix: new HarmonyMethod(typeof(RenderProfiler), nameof(OnGui)));
        Log.Message("[Render profiler] Ready: Ctrl+Shift+F8 records 15 seconds locally; " + names.Count + " stages. Off until triggered.");
    }
    private static void Begin(MethodBase __originalMethod, out RenderTimingSession.Token __state)
    {
        __state = default;
        if (!Active || System.Threading.Thread.CurrentThread.ManagedThreadId != mainThread) return;
        if (indices.TryGetValue(__originalMethod, out int index)) __state = session.Begin(index, Stopwatch.GetTimestamp());
    }
    private static Exception End(Exception __exception, RenderTimingSession.Token __state)
    {
        if (Active && System.Threading.Thread.CurrentThread.ManagedThreadId == mainThread)
            session.End(__state, Stopwatch.GetTimestamp());
        return __exception;
    }
    public static void Start()
    {
        if (session == null || Active) return;
        frames = samples = 0; lastFrame = -1;
        metadata = $"Architecture: {RuntimeInformation.ProcessArchitecture}\nResolution: {Screen.width}x{Screen.height}, UI scale: {Prefs.UIScale}\nGPU: {SystemInfo.graphicsDeviceName}\nVSync: {QualitySettings.vSyncCount}, target FPS: {Application.targetFrameRate}\nMap: {Find.CurrentMap?.uniqueID}, pawns on map: {Find.CurrentMap?.mapPawns.AllPawnsSpawned.Count}\nDesync tracing mode: {Multiplayer.settings?.desyncTracingMode}";
        started = Stopwatch.GetTimestamp(); session.Start();
        Log.Message("[Render profiler] Recording 15 seconds; keep the same camera and speed. Ctrl+Shift+F8 stops early.");
    }
    private static void OnGui()
    {
        var ev = Event.current;
        if (ev?.type == EventType.KeyDown && ev.control && ev.shift && ev.keyCode == KeyCode.F8)
        {
            if (Active) Stop(); else Start();
            ev.Use();
        }
        if (!Active) return;
        if (lastFrame != Time.frameCount)
        {
            lastFrame = Time.frameCount; frames++;
            if (samples < MaxFrames) frameMs[samples++] = Time.unscaledDeltaTime * 1000f;
        }
        long elapsed = Stopwatch.GetTimestamp() - started;
        if (elapsed >= Stopwatch.Frequency * 15L) Stop();
        else GUI.Label(new Rect(12f, 12f, 500f, 24f), "Rendering profile: recording (15 seconds; Ctrl+Shift+F8 to stop)");
    }
    public static string Stop()
    {
        if (!Active) return null;
        session.Stop();
        double seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        try
        {
            var report = new StringBuilder("RimWorld local rendering profile\n").AppendLine(metadata)
                .AppendLine($"Duration: {seconds:F2}s; observed frames: {frames}; frame samples: {samples}")
                .AppendLine("Main-thread elapsed timings (including waits), not GPU timestamps. Inclusive timings overlap; exclusive timings subtract instrumented child calls only.")
                .AppendLine("Sampling covers selected stages and component hooks, not every renderer/mod method. Timers add overhead; compare captures with identical settings.")
                .AppendLine($"Dropped nesting samples: {session.Dropped}");
            if (frames > samples) report.AppendLine("Frame sample cap reached; frame-time statistics cover the first " + samples + " frames only.");
            if (samples > 0)
            {
                var sorted = new float[samples]; Array.Copy(frameMs, sorted, samples); Array.Sort(sorted);
                report.AppendLine($"Frame time ms: mean={sorted.Average():F2}, median={sorted[samples / 2]:F2}, p95={sorted[(int)((samples - 1) * .95)]:F2}");
            }
            double ms = 1000d / Stopwatch.Frequency;
            var stats = session.Snapshot();
            report.AppendLine("Calls\tExclusive ms\tInclusive ms\tExclusive ms/frame\tMax call ms\tAssembly :: Method");
            foreach (var entry in stats.Select((stat, i) => (stat, i)).OrderByDescending(x => x.stat.Exclusive))
            {
                if (entry.stat.Calls == 0) continue;
                report.AppendLine($"{entry.stat.Calls}\t{entry.stat.Exclusive * ms:F3}\t{entry.stat.Inclusive * ms:F3}\t{entry.stat.Exclusive * ms / Math.Max(1, frames):F3}\t{entry.stat.Max * ms:F3}\t{names[entry.i]}");
            }
            report.AppendLine("\nCoverage notes:");
            foreach (string note in coverage) report.AppendLine(note);
            Directory.CreateDirectory(Multiplayer.LogsDir);
            string path = Path.Combine(Multiplayer.LogsDir, "RenderProfile-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            File.WriteAllText(path, report.ToString());
            Log.Message("[Render profiler] Saved " + path);
            return path;
        }
        catch (Exception e) { Log.Warning("[Render profiler] Could not write report: " + e.Message); return null; }
    }
}
