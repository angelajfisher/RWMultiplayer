using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace Multiplayer.Client.Desyncs;

// Loaded explicitly from the mod directory: Unity's plugin search path does not
// include arbitrary mod folders. Keep the handle alive for the process lifetime.
public static class MacStackTracing
{
    public static bool Selected { get; private set; }
    public static bool Available => capture != null;
    private static IntPtr library;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate uint Capture(long* frames, uint capacity, uint skip);
    private static Capture? capture;

    public static void Initialize(string path)
    {
        Selected = true;
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("macOS tracing currently requires an x86-64 process (Rosetta on Apple Silicon)");
        library = dlopen(path, 2); // RTLD_NOW
        if (library == IntPtr.Zero)
            throw new DllNotFoundException(Marshal.PtrToStringAnsi(dlerror()));
        var symbol = dlsym(library, "mp_capture_stack");
        if (symbol == IntPtr.Zero)
            throw new EntryPointNotFoundException("mp_capture_stack");
        capture = Marshal.GetDelegateForFunctionPointer<Capture>(symbol);
        try
        {
            if (!CapturesManagedFrames())
                throw new NotSupportedException("The native unwinder could not capture Mono managed frames");
        }
        catch
        {
            capture = null;
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CapturesManagedFrames()
    {
        var frames = new long[256];
        int count = Trace(frames, 0);
        var method = typeof(MacStackTracing).GetMethod(nameof(CapturesManagedFrames),
            BindingFlags.Static | BindingFlags.NonPublic)!.MethodHandle.Value;
        for (int i = 0; i < count; i++)
        {
            var info = Native.mono_jit_info_table_find(Native.DomainPtr, (IntPtr)frames[i]);
            if (info != IntPtr.Zero && Native.mono_jit_info_get_method(info) == method)
                return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static unsafe int Trace(long[] frames, int skip, int maxFrames = int.MaxValue)
    {
        int capacity = Math.Min(frames.Length, maxFrames);
        if (capture == null || capacity <= 0) return 0;
        fixed (long* ptr = frames)
            return (int)capture(ptr, (uint)capacity, (uint)Math.Max(0, skip));
    }

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlopen(string path, int mode);
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlerror();
}
