using Multiplayer.Common;

namespace Tests;

public class TraceFrameStoreTest
{
    [Test]
    public void RepeatedFramesShareStorageButDoNotRetainCaptureBuffers()
    {
        var store = new TraceFrameStore();
        long[] capture = [10, 20, 30, 99];
        var a = store.Acquire(capture, 3);
        var b = store.Acquire(new long[] { 10, 20, 30, 88 }, 3);
        Assert.That(b, Is.SameAs(a));
        capture[0] = 55;
        Assert.That(a.Frames, Is.EqualTo(new long[] { 10, 20, 30 }));
        store.Release(a);
        Assert.That(store.Count, Is.EqualTo(1));
        store.Release(b);
        Assert.That(store.Count, Is.Zero);
    }

    [Test]
    public void DepthAndHashCollisionsDoNotMergeDifferentStacks()
    {
        var store = new TraceFrameStore();
        // Identical GetHashCode values on long must still compare full frames.
        var a = store.Acquire(new long[] { 0 }, 1);
        var b = store.Acquire(new long[] { 0x100000001 }, 1);
        var c = store.Acquire(new long[] { 0, 0 }, 2);
        Assert.That(a, Is.Not.SameAs(b));
        Assert.That(a, Is.Not.SameAs(c));
        Assert.That(store.Count, Is.EqualTo(3));
        store.Release(a); store.Release(b); store.Release(c);
        Assert.That(store.Count, Is.Zero);
    }

    [Test]
    public void RingEvictionsReleaseOnlyUnusedFrameDefinitions()
    {
        var store = new TraceFrameStore();
        var ring = new HeadTailBuffer<TraceFrameStore.Entry>(2, 3, store.Release);
        for (int i = 0; i < 100000; i++) ring.Add(store.Acquire(new long[] { i % 9 }, 1));
        Assert.That(store.Count, Is.LessThanOrEqualTo(5));
        ring.Clear(); ring.Clear();
        Assert.That(store.Count, Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Acquire(new long[1], 2));
    }
}
