using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Client.Desyncs;
using Multiplayer.Common;

static class Program
{
    public static int Main(string[] args)
    {
        Native.EarlyInit(Native.NativeOS.OSX);
        Native.InitLmfPtr(Native.NativeOS.OSX);
        Native.mini_parse_debug_option("disable_omit_fp");
        if (Native.LmfPtr != 0) throw new Exception("macOS must not use the legacy LMF offset");
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            try
            {
                MacStackTracing.Initialize(args[0]);
                throw new Exception("Unsupported architecture unexpectedly enabled tracing");
            }
            catch (PlatformNotSupportedException)
            {
                if (!MacStackTracing.Selected || MacStackTracing.Available)
                    throw new Exception("Unsupported architecture did not fail closed");
                int hash = 0;
                if (DeferredStackTracingImpl.TraceImpl(new long[32], ref hash) != 0)
                    throw new Exception("Unsupported architecture returned a trace");
                Console.WriteLine("PASS: unsupported architecture disables tracing without touching the legacy walker");
                return 0;
            }
        }
        try
        {
            MacStackTracing.Initialize(args[0] + ".missing");
            throw new Exception("Missing library unexpectedly loaded");
        }
        catch (DllNotFoundException)
        {
            if (!MacStackTracing.Selected || MacStackTracing.Available)
                throw new Exception("Missing library did not fail closed");
        }
        MacStackTracing.Initialize(args[0]);
        if (Capture() != 0) return 1;
        var original = typeof(Program).GetMethod(nameof(PatchedTarget), BindingFlags.Static | BindingFlags.NonPublic);
        var replacement = new Harmony("multiplayer.mono-tracing-smoke").Patch(original,
            postfix: new HarmonyMethod(typeof(Program).GetMethod(nameof(PatchedPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
        Native.HarmonyOriginalGetter = address => address == replacement.MethodHandle.GetFunctionPointer().ToInt64() ? original : null;
        PatchedTarget();
        GenericFrame<int>.Run();
        DeepStack(150);
        var timer = Stopwatch.StartNew();
        var frames = new long[32];
        for (int i = 0; i < 10000; i++)
        {
            int hash = 0;
            if (DeferredStackTracingImpl.TraceImpl(frames, ref hash) == 0)
                throw new Exception("Capture unexpectedly returned no managed frames");
        }
        timer.Stop();
        Console.WriteLine($"PASS: 10000 warm captures in {timer.ElapsedMilliseconds} ms (smoke-test stack only)");
        var budget = new DesyncTraceBudget();
        timer.Restart();
        int captures = 0;
        for (int i = 0; i < 5_000_000; i++)
        {
            if (!budget.TryCapture(123, out _)) continue;
            int hash = 0;
            if (DeferredStackTracingImpl.TraceImpl(frames, ref hash) == 0)
                throw new Exception("Sampled burst capture returned no frames");
            captures++;
        }
        timer.Stop();
        if (captures != 35) throw new Exception($"Unexpected burst capture count: {captures}");
        Console.WriteLine($"PASS: 5000000 burst events, {captures} managed stack captures in {timer.ElapsedMilliseconds} ms");
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int DeepStack(int remaining)
    {
        if (remaining > 0) return remaining + DeepStack(remaining - 1);
        int hash = 0;
        var frames = new long[128];
        int count = DeferredStackTracingImpl.TraceImpl(frames, ref hash);
        if (count != frames.Length) throw new Exception($"Deep trace truncated: {count}");
        Console.WriteLine("PASS: deep-stack retry preserves requested 128 managed frames");
        return count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Capture()
    {
        var frames = new long[128];
        int hash = 0;
        int count = DeferredStackTracingImpl.TraceImpl(frames, ref hash);
        int repeatedHash = 0;
        int repeatedCount = DeferredStackTracingImpl.TraceImpl(new long[128], ref repeatedHash);
        if (count != repeatedCount || hash != repeatedHash)
            throw new Exception("Cold and cached trace captures disagree");
        bool foundCaller = false;
        for (int i = 0; i < count; i++)
        {
            string name = Native.MethodNameNormalizedFromAddr(frames[i], false);
            Console.WriteLine(name ?? "<native>");
            if (Native.MethodNameFromAddr(frames[i], false) == null)
                throw new Exception("Captured frame could not be rendered in a desync report");
            foundCaller |= name != null && name.Contains("Program.Capture(");
        }
        Console.WriteLine($"Captured {count} frames; hash {hash}; managed caller found: {foundCaller}");
        return foundCaller ? 0 : 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void PatchedTarget() { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void PatchedPostfix()
    {
        var frames = new long[32];
        int hash = 0;
        int count = DeferredStackTracingImpl.TraceImpl(frames, ref hash, skipFrames: 1);
        bool foundOriginal = false;
        for (int i = 0; i < count; i++)
            foundOriginal |= Native.MethodNameNormalizedFromAddr(frames[i], true)?.Contains("Program.PatchedTarget(") == true;
        if (!foundOriginal) throw new Exception("Harmony replacement was not normalized to its original method");
        Console.WriteLine("PASS: Harmony original-method normalization and skipFrames");
    }

    static class GenericFrame<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Run()
        {
            int hash = 0;
            var frames = new long[32];
            int count = DeferredStackTracingImpl.TraceImpl(frames, ref hash);
            bool foundGeneric = false;
            for (int i = 0; i < count; i++)
                foundGeneric |= Native.MethodNameNormalizedFromAddr(frames[i], true)?.Contains("GenericFrame") == true;
            if (!foundGeneric) throw new Exception("Generic managed caller was not captured");
            Console.WriteLine("PASS: generic managed caller and method metadata");
        }
    }
}
