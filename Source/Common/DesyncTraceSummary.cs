using System;
using System.Collections.Generic;

namespace Multiplayer.Common;

// A digest covers every captured stack, including stacks evicted from the ring.
// Head and tail hashes locate differences still available as full stack traces.
public sealed class DesyncTraceSummary
{
    public const int Capacity = 2048;
    public const int HeadCapacity = 256;
    public const int HeaderLength = 6;
    public const int MaxWireHashes = HeaderLength + HeadCapacity + Capacity;
    public const int EarlierHistoryDiffers = -2;
    private const int Magic = 0x54524333; // TRC3
    private const ulong InitialHash = 14695981039346656037UL;

    private readonly HeadTailBuffer<int> recent = new(HeadCapacity, Capacity);
    private ulong digest = InitialHash;
    public long TotalCaptures { get; private set; }

    public void Add(int hash)
    {
        digest = unchecked((digest ^ (uint)hash) * 1099511628211UL);
        TotalCaptures++;
        recent.Add(hash);
    }

    public List<int> ToWire()
    {
        if (TotalCaptures == 0) return new List<int>();
        var result = new List<int>(HeaderLength + recent.Count)
        {
            Magic, unchecked((int)TotalCaptures), (int)(TotalCaptures >> 32),
            unchecked((int)digest), unchecked((int)(digest >> 32)), recent.HeadCount
        };
        foreach (var entry in recent.Entries()) result.Add(entry.Value);
        return result;
    }

    public void Clear()
    {
        recent.Clear();
        TotalCaptures = 0;
        digest = InitialHash;
    }

    private static bool HasHeader(IReadOnlyList<int> hashes) =>
        hashes.Count >= HeaderLength && hashes[0] == Magic && hashes[5] >= 0 &&
        hashes[5] <= HeadCapacity && hashes.Count >= HeaderLength + hashes[5] &&
        DecodeCount(hashes) >= hashes.Count - HeaderLength;
    private const int LegacyMagic = 0x54524332;
    private static bool HasLegacyHeader(IReadOnlyList<int> hashes) => hashes.Count >= 5 &&
        hashes[0] == LegacyMagic && DecodeCount(hashes) >= hashes.Count - 5;
    private static long DecodeCount(IReadOnlyList<int> hashes) =>
        unchecked((long)((uint)hashes[1] | ((ulong)(uint)hashes[2] << 32)));
    public static long CaptureCount(IReadOnlyList<int> hashes) =>
        HasHeader(hashes) || HasLegacyHeader(hashes) ? DecodeCount(hashes) : hashes.Count;

    private static IEnumerable<KeyValuePair<long, int>> Entries(IReadOnlyList<int> hashes)
    {
        int header = HasHeader(hashes) ? HeaderLength : HasLegacyHeader(hashes) ? 5 : 0;
        int head = header == HeaderLength ? hashes[5] : 0;
        for (int i = 0; i < head; i++) yield return new(i, hashes[header + i]);
        long tailStart = CaptureCount(hashes) - (hashes.Count - header - head);
        for (int i = header + head; i < hashes.Count; i++) yield return new(tailStart + i - header - head, hashes[i]);
    }

    public static int FindFirstDifference(IReadOnlyList<int> left, IReadOnlyList<int> right, out bool found)
    {
        using var a = Entries(left).GetEnumerator();
        using var b = Entries(right).GetEnumerator();
        bool hasA = a.MoveNext(), hasB = b.MoveNext();
        while (hasA && hasB)
        {
            if (a.Current.Key < b.Current.Key) { hasA = a.MoveNext(); continue; }
            if (b.Current.Key < a.Current.Key) { hasB = b.MoveNext(); continue; }
            if (a.Current.Value != b.Current.Value)
            {
                found = true;
                return a.Current.Key <= int.MaxValue ? (int)a.Current.Key : EarlierHistoryDiffers;
            }
            hasA = a.MoveNext(); hasB = b.MoveNext();
        }
        found = false;
        long leftCount = CaptureCount(left), rightCount = CaptureCount(right);
        if (leftCount != rightCount)
        {
            long end = Math.Min(leftCount, rightCount);
            return end <= int.MaxValue ? (int)end : EarlierHistoryDiffers;
        }
        if ((HasHeader(left) || HasLegacyHeader(left)) && (HasHeader(right) || HasLegacyHeader(right)) &&
            (left[3] != right[3] || left[4] != right[4]))
        {
            found = true; return EarlierHistoryDiffers;
        }
        return -1;
    }
}
