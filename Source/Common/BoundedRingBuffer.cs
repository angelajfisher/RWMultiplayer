using System;
using System.Collections;
using System.Collections.Generic;

namespace Multiplayer.Common;

// Oldest-to-newest enumeration with O(1) insertion and immediate release of evictions.
public sealed class BoundedRingBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] items;
    private readonly Action<T>? release;
    private int start;

    public int Count { get; private set; }
    public int Capacity => items.Length;

    public BoundedRingBuffer(int capacity, Action<T>? release = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        items = new T[capacity];
        this.release = release;
    }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            return items[(start + index) % Capacity];
        }
    }

    public void Add(T item)
    {
        if (Count < Capacity)
            items[(start + Count++) % Capacity] = item;
        else
        {
            T evicted = items[start];
            items[start] = item;
            start = (start + 1) % Capacity;
            release?.Invoke(evicted);
        }
    }

    public void Clear()
    {
        if (release != null)
            for (int i = 0; i < Count; i++) release(this[i]);
        Array.Clear(items, 0, items.Length);
        Count = start = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
