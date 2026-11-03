using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Multiplayer.Client;
using Multiplayer.Client.Desyncs;

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
        return Capture();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Capture()
    {
        var frames = new long[128];
        int hash = 0;
        int count = DeferredStackTracingImpl.TraceImpl(frames, ref hash);
        bool foundCaller = false;
        for (int i = 0; i < count; i++)
        {
            string name = Native.MethodNameNormalizedFromAddr(frames[i], false);
            Console.WriteLine(name ?? "<native>");
            foundCaller |= name != null && name.Contains("Program:Capture");
        }
        Console.WriteLine($"Captured {count} frames; hash {hash}; managed caller found: {foundCaller}");
        return foundCaller ? 0 : 1;
    }
}
