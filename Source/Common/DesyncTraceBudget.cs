namespace Multiplayer.Common;

// Sampling depends only on simulation tick and event ordinal, never wall time or
// machine speed. A slow peer must select the same events as a fast peer.
public sealed class DesyncTraceBudget
{
    public const int FullTracesPerTick = 4096;
    public const int MaxTracesPerTick = FullTracesPerTick + 64;

    private int? currentTick;
    private long tickEvents;
    private int tickCaptures;

    public long EventsSeen { get; private set; }
    public long Captures { get; private set; }
    public long EventsSkipped => EventsSeen - Captures;

    public bool TryCapture(int tick, out long eventNumber)
    {
        eventNumber = ++EventsSeen;
        if (currentTick != tick)
        {
            currentTick = tick;
            tickEvents = 0;
            tickCaptures = 0;
        }

        long ordinal = tickEvents++;
        // Ordinary ticks get full stacks. Only oversized ticks switch to sparse
        // sampling at 4096, 8192, ... . Storage is bounded separately by the ring.
        bool selected = ordinal < FullTracesPerTick || (ordinal & (ordinal - 1)) == 0;
        if (!selected || tickCaptures >= MaxTracesPerTick)
            return false;

        tickCaptures++;
        Captures++;
        return true;
    }
}
