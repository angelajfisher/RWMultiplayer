using System;
namespace Multiplayer.Common;

public sealed class TickTimingCounter
{
    public long Calls, AdvancedTicks, Elapsed, Resets, NoAdvance, Exceptions;
    public void Add(int before, int after, long elapsed, bool failed)
    {
        Calls++; Elapsed += Math.Max(0, elapsed);
        if (failed) Exceptions++;
        long delta = (long)after - before;
        if (delta < 0) Resets++;
        else if (delta == 0) NoAdvance++;
        else AdvancedTicks += delta;
    }
    public double? TimePerTick => AdvancedTicks > 0 && Resets == 0 ? Elapsed / (double)AdvancedTicks : null;
    public static double? Rate(int before, int after, double seconds) =>
        after >= before && seconds > 0 ? ((long)after - before) / seconds : null;
}
