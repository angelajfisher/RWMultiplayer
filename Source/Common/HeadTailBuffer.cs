using System;
using System.Collections.Generic;

namespace Multiplayer.Common;

// Fixed beginning + rolling end, with explicit global indices across any evicted gap.
public sealed class HeadTailBuffer<T>
{
    private readonly int headCapacity;
    private readonly List<T> head = new();
    private readonly BoundedRingBuffer<T> tail;
    private readonly Action<T>? release;
    public long TotalCount { get; private set; }
    public int Count => head.Count + tail.Count;
    public int HeadCount => head.Count;
    public long TailStart => TotalCount - tail.Count;

    public HeadTailBuffer(int headCapacity, int tailCapacity, Action<T>? release = null)
    {
        if (headCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(headCapacity));
        this.headCapacity = headCapacity;
        this.release = release;
        tail = new BoundedRingBuffer<T>(tailCapacity, release);
    }

    public void Add(T item)
    {
        if (head.Count < headCapacity) head.Add(item);
        else tail.Add(item);
        TotalCount++;
    }

    public bool TryGet(long index, out T item)
    {
        if (index >= 0 && index < head.Count) { item = head[(int)index]; return true; }
        if (index >= TailStart && index < TotalCount) { item = tail[(int)(index - TailStart)]; return true; }
        item = default!;
        return false;
    }

    public IEnumerable<KeyValuePair<long, T>> Entries()
    {
        for (int i = 0; i < head.Count; i++) yield return new(i, head[i]);
        for (int i = 0; i < tail.Count; i++) yield return new(TailStart + i, tail[i]);
    }

    public void Clear()
    {
        if (release != null) foreach (var item in head) release(item);
        head.Clear(); tail.Clear(); TotalCount = 0;
    }
}
