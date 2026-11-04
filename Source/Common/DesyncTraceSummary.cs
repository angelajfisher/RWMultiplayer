using System;
using System.Collections.Generic;

namespace Multiplayer.Common;

// A digest covers every captured stack, including stacks evicted from the ring.
// The tail hashes locate differences that are still available as full stack traces.
public sealed class DesyncTraceSummary
{
    public const int Capacity = 2048;
    public const int HeaderLength = 5;
    public const int MaxWireHashes = HeaderLength + Capacity;
    public const int EarlierHistoryDiffers = -2;
    private const int Magic = 0x54524332; // TRC2
    private const ulong InitialHash = 14695981039346656037UL;

    private readonly BoundedRingBuffer<int> recent = new(Capacity);
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
            unchecked((int)digest), unchecked((int)(digest >> 32))
        };
        result.AddRange(recent);
        return result;
    }

    public void Clear()
    {
        recent.Clear();
        TotalCaptures = 0;
        digest = InitialHash;
    }

    private static bool HasHeader(IReadOnlyList<int> hashes) =>
        hashes.Count >= HeaderLength && hashes[0] == Magic &&
        DecodeCount(hashes) >= hashes.Count - HeaderLength;

    private static long DecodeCount(IReadOnlyList<int> hashes) =>
        unchecked((long)((uint)hashes[1] | ((ulong)(uint)hashes[2] << 32)));

    // Legacy report files have bare hashes; keep those readable too.
    public static long CaptureCount(IReadOnlyList<int> hashes) =>
        HasHeader(hashes) ? DecodeCount(hashes) : hashes.Count;

    public static int FindFirstDifference(IReadOnlyList<int> left, IReadOnlyList<int> right, out bool found)
    {
        bool leftHeader = HasHeader(left), rightHeader = HasHeader(right);
        long leftCount = CaptureCount(left), rightCount = CaptureCount(right);
        long leftStart = leftHeader ? leftCount - (left.Count - HeaderLength) : 0;
        long rightStart = rightHeader ? rightCount - (right.Count - HeaderLength) : 0;
        long end = Math.Min(leftCount, rightCount);
        for (long index = Math.Max(leftStart, rightStart); index < end; index++)
        {
            int a = (leftHeader ? HeaderLength : 0) + (int)(index - leftStart);
            int b = (rightHeader ? HeaderLength : 0) + (int)(index - rightStart);
            if (left[a] != right[b])
            {
                found = true;
                return index <= int.MaxValue ? (int)index : EarlierHistoryDiffers;
            }
        }

        found = false;
        if (leftCount != rightCount)
            return end <= int.MaxValue ? (int)end : EarlierHistoryDiffers;
        if (leftHeader && rightHeader && (left[3] != right[3] || left[4] != right[4]))
        {
            found = true;
            return EarlierHistoryDiffers;
        }
        return -1;
    }
}
