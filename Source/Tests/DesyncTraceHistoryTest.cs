using Multiplayer.Common;

namespace Tests;

public class DesyncTraceHistoryTest
{
    [Test]
    public void RingWrapsInOrderAndReleasesEachItemExactlyOnce()
    {
        var released = new List<int>();
        var ring = new BoundedRingBuffer<int>(3, released.Add);
        for (int i = 0; i < 10; i++) ring.Add(i);
        Assert.That(ring, Is.EqualTo(new[] { 7, 8, 9 }));
        Assert.That(released, Is.EqualTo(Enumerable.Range(0, 7)));
        Assert.That(ring[0], Is.EqualTo(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = ring[3]);
        ring.Clear();
        ring.Clear();
        Assert.That(released, Is.EqualTo(Enumerable.Range(0, 10)));
        ring.Add(99);
        Assert.That(ring, Is.EqualTo(new[] { 99 }));
    }

    [Test]
    public void EvictedMismatchStillChangesRollingDigest()
    {
        var left = Build(10000);
        var right = Build(10000, differingIndex: 1);
        var a = left.ToWire();
        var b = right.ToWire();
        Assert.That(a.Count, Is.EqualTo(DesyncTraceSummary.MaxWireHashes));
        Assert.That(a.Skip(DesyncTraceSummary.HeaderLength), Is.EqualTo(b.Skip(DesyncTraceSummary.HeaderLength)));
        Assert.That(a, Is.Not.EqualTo(b));
        int diff = DesyncTraceSummary.FindFirstDifference(a, b, out bool found);
        Assert.That(found, Is.True);
        Assert.That(diff, Is.EqualTo(DesyncTraceSummary.EarlierHistoryDiffers));
    }

    [Test]
    public void RetainedMismatchUsesGlobalCaptureIndexAfterWrap()
    {
        var left = Build(10000);
        var right = Build(10000, differingIndex: 9997);
        int diff = DesyncTraceSummary.FindFirstDifference(left.ToWire(), right.ToWire(), out bool found);
        Assert.That(found, Is.True);
        Assert.That(diff, Is.EqualTo(9997));
    }

    [Test]
    public void DifferentLengthsAlignTheOverlappingRingTails()
    {
        var left = Build(10000);
        var right = Build(9990);
        int diff = DesyncTraceSummary.FindFirstDifference(left.ToWire(), right.ToWire(), out bool found);
        Assert.That(found, Is.False);
        Assert.That(diff, Is.EqualTo(9990));
    }

    [Test]
    public void IdenticalSequencesStayEqualAcrossRingWrapAndClear()
    {
        var left = Build(10000);
        var right = Build(10000);
        Assert.That(left.ToWire(), Is.EqualTo(right.ToWire()));
        Assert.That(DesyncTraceSummary.FindFirstDifference(left.ToWire(), right.ToWire(), out _), Is.EqualTo(-1));
        left.Clear();
        Assert.That(left.ToWire(), Is.Empty);
        left.Add(123);
        var fresh = new DesyncTraceSummary();
        fresh.Add(123);
        Assert.That(left.ToWire(), Is.EqualTo(fresh.ToWire()));
    }

    [Test]
    public void HistoricalReportsWithBareHashesRemainReadable()
    {
        int[] left = [1, 2, 3], right = [1, 9, 3];
        Assert.That(DesyncTraceSummary.CaptureCount(left), Is.EqualTo(3));
        Assert.That(DesyncTraceSummary.FindFirstDifference(left, right, out bool found), Is.EqualTo(1));
        Assert.That(found, Is.True);
    }

    private static DesyncTraceSummary Build(int count, int differingIndex = -1)
    {
        var summary = new DesyncTraceSummary();
        for (int i = 0; i < count; i++) summary.Add(i == differingIndex ? ~i : i);
        return summary;
    }
}
