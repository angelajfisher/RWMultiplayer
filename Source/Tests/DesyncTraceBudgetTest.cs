using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;

namespace Tests;

public class DesyncTraceBudgetTest
{
    [Test]
    public void MultiMillionEventBurstKeepsEarlyAndLateContextWithBoundedCaptures()
    {
        const int events = 5_000_000;
        var budget = new DesyncTraceBudget();
        var selectedEvents = new List<long>();
        for (int i = 0; i < events; i++)
            if (budget.TryCapture(100, out long eventNumber)) selectedEvents.Add(eventNumber);

        Assert.That(selectedEvents.Take(4096), Is.EqualTo(Enumerable.Range(1, 4096).Select(i => (long)i)));
        Assert.That(selectedEvents.Count, Is.EqualTo(4107));
        Assert.That(selectedEvents.Last(), Is.EqualTo(4_194_305));
        Assert.That(budget.EventsSeen, Is.EqualTo(events));
        Assert.That(budget.EventsSkipped, Is.EqualTo(events - selectedEvents.Count));
    }

    [Test]
    public void OrdinaryTicksRemainFullyCapturedEvenAfterRingCapacityAndBurst()
    {
        var budget = new DesyncTraceBudget();
        for (int tick = 0; tick < 30; tick++)
            for (int i = 0; i < 1000; i++)
                Assert.That(budget.TryCapture(tick, out _), Is.True);

        Assert.That(budget.Captures, Is.EqualTo(30000));
        for (int i = 0; i < 100_000; i++) budget.TryCapture(30, out _);
        Assert.That(budget.EventsSkipped, Is.GreaterThan(0));
        for (int i = 0; i < 1000; i++)
            Assert.That(budget.TryCapture(31, out _), Is.True);
    }

    [Test]
    public void TickBoundaryResetsSamplingWithinInterval()
    {
        var budget = new DesyncTraceBudget();
        for (int i = 0; i < 100_000; i++) budget.TryCapture(10, out _);
        long before = budget.Captures;
        for (int i = 0; i < 16; i++)
            Assert.That(budget.TryCapture(11, out _), Is.True);
        Assert.That(budget.Captures, Is.EqualTo(before + 16));
    }

    [Test]
    public void MaximumTracePacketStaysSmallAndPreservesRandomStateChecks()
    {
        var summary = new DesyncTraceSummary();
        for (int i = 0; i < 30000; i++) summary.Add(i);
        var opinion = MakeOpinion(0);
        opinion.traceHashes = summary.ToWire();
        var binder = BinderOf.Identity<SyncOpinion>();
        var bytes = binder.Serialize(opinion);
        var roundtrip = binder.Deserialize(bytes);

        Assert.That(bytes.Length, Is.LessThan(10 * 1024));
        Assert.That(DesyncTraceSummary.CaptureCount(roundtrip.traceHashes), Is.EqualTo(30000));
        Assert.That(roundtrip.traceHashes, Is.EqualTo(opinion.traceHashes));
        Assert.That(roundtrip.commandRandomStates, Is.EqualTo(opinion.commandRandomStates));
        Assert.That(roundtrip.worldRandomStates, Is.EqualTo(opinion.worldRandomStates));
        Assert.That(roundtrip.mapRandomStates[0].randomStates, Is.EqualTo(opinion.mapRandomStates[0].randomStates));
    }

    [Test]
    public void OversizedDiagnosticListCannotBeSent()
    {
        var opinion = MakeOpinion(DesyncTraceSummary.MaxWireHashes + 1);
        Assert.Throws<WriterException>(() => BinderOf.Identity<SyncOpinion>().Serialize(opinion));
    }

    private static SyncOpinion MakeOpinion(int traceCount) => new()
    {
        startTick = 30,
        commandRandomStates = [101, 102],
        worldRandomStates = [201, 202],
        mapRandomStates = [new MapRandomState { mapId = 7, randomStates = [301, 302] }],
        traceHashes = Enumerable.Range(0, traceCount).ToList()
    };
}
