using System;

namespace Multiplayer.Common;

// Allocation-free timing while active. No game state, RNG, or wall-clock policy.
public sealed class RenderTimingSession
{
    public readonly struct Token(int epoch, int depth, bool valid)
    {
        internal readonly int Epoch = epoch, Depth = depth;
        internal readonly bool Valid = valid;
    }
    public struct Stat
    {
        public long Calls, Inclusive, Exclusive, Max;
    }
    private struct Frame { internal int Key; internal long Start, Children; }
    private readonly Frame[] stack;
    private readonly Stat[] stats;
    private int epoch, depth;
    public bool Active { get; private set; }
    public int Dropped { get; private set; }
    public RenderTimingSession(int targets, int maxDepth = 128)
    {
        if (targets < 1 || maxDepth < 1) throw new ArgumentOutOfRangeException();
        stats = new Stat[targets]; stack = new Frame[maxDepth];
    }
    public void Start()
    {
        Active = false;
        Array.Clear(stats, 0, stats.Length); depth = 0; Dropped = 0; epoch++;
        Active = true;
    }
    public Token Begin(int key, long now)
    {
        if (!Active) return default;
        if (key < 0 || key >= stats.Length || depth == stack.Length) { Dropped++; return default; }
        int slot = depth++;
        stack[slot] = new Frame { Key = key, Start = now };
        return new Token(epoch, slot, true);
    }
    public void End(Token token, long now)
    {
        if (!Active || !token.Valid || token.Epoch != epoch) return;
        if (depth != token.Depth + 1) { Dropped++; depth = 0; return; }
        var frame = stack[--depth];
        long elapsed = Math.Max(0, now - frame.Start);
        ref var stat = ref stats[frame.Key];
        stat.Calls++; stat.Inclusive += elapsed;
        stat.Exclusive += Math.Max(0, elapsed - frame.Children);
        stat.Max = Math.Max(stat.Max, elapsed);
        if (depth > 0) stack[depth - 1].Children += elapsed;
    }
    public void Stop() { Active = false; depth = 0; }
    public Stat[] Snapshot() => (Stat[])stats.Clone();
}
