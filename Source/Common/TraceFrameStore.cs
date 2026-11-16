using System;
using System.Collections.Generic;

namespace Multiplayer.Common;

// Per-history interning, with structural equality (not just a stack hash).
// Entries disappear when the last retained event using them is evicted.
public sealed class TraceFrameStore
{
    public sealed class Entry
    {
        public readonly long[] Frames;
        internal int References;
        internal Entry(long[] frames) { Frames = frames; }
    }
    private sealed class FrameComparer : IEqualityComparer<long[]>
    {
        public bool Equals(long[]? a, long[]? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        public int GetHashCode(long[] frames)
        {
            int hash = 17;
            foreach (long frame in frames) hash = unchecked(hash * 31 + frame.GetHashCode());
            return hash;
        }
    }
    private readonly Dictionary<long[], Entry> entries = new(new FrameComparer());
    public int Count => entries.Count;
    // Reused lookup storage avoids allocating an array for every repeated stack.
    private readonly Dictionary<int, long[]> probes = new();
    public Entry Acquire(long[] frames, int depth)
    {
        if (depth < 0 || depth > frames.Length) throw new ArgumentOutOfRangeException(nameof(depth));
        if (!probes.TryGetValue(depth, out var probe)) probes[depth] = probe = new long[depth];
        Array.Copy(frames, probe, depth);
        if (!entries.TryGetValue(probe, out var entry))
        {
            var owned = (long[])probe.Clone();
            entries.Add(owned, entry = new Entry(owned));
        }
        entry.References++;
        return entry;
    }
    public void Release(Entry entry)
    {
        if (--entry.References == 0) entries.Remove(entry.Frames);
    }
}
