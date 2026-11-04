namespace Multiplayer.Common;

// Sampling depends only on simulation tick and event ordinal, never wall time or
// machine speed. A slow peer must select the same events as a fast peer.
public sealed class DesyncTraceBudget
{
    public const int InitialTracesPerTick = 16;
    public const int MaxTracesPerTick = 64;
    public const int MaxTracesPerOpinion = 2048;

    private int? currentTick;
    private long tickEvents;
    private int tickCaptures;

    public long EventsSeen { get; private set; }
    public int Captures { get; private set; }
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
        // Keep early context, then sample 16, 32, 64, ... rather than spending
        // millions of stack walks on one event-generation burst.
        bool selected = ordinal < InitialTracesPerTick || (ordinal & (ordinal - 1)) == 0;
        if (!selected || tickCaptures >= MaxTracesPerTick || Captures >= MaxTracesPerOpinion)
            return false;

        tickCaptures++;
        Captures++;
        return true;
    }
}
