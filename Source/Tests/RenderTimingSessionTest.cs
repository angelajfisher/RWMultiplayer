using Multiplayer.Common;

namespace Tests;

public class RenderTimingSessionTest
{
    [Test]
    public void DisabledSessionDoesNotRecord()
    {
        var session = new RenderTimingSession(1);
        var token = session.Begin(0, 10); session.End(token, 20);
        Assert.That(session.Snapshot()[0].Calls, Is.Zero);
    }
    [Test]
    public void NestedExclusiveTimeAvoidsDoubleCounting()
    {
        var session = new RenderTimingSession(2); session.Start();
        var parent = session.Begin(0, 10); var child = session.Begin(1, 12);
        session.End(child, 15); session.End(parent, 20);
        var stats = session.Snapshot();
        Assert.Multiple(() => {
            Assert.That(stats[0].Inclusive, Is.EqualTo(10));
            Assert.That(stats[0].Exclusive, Is.EqualTo(7));
            Assert.That(stats[1].Exclusive, Is.EqualTo(3));
            Assert.That(stats[0].Exclusive + stats[1].Exclusive, Is.EqualTo(10));
        });
    }
    [Test]
    public void RecursionAndOverflowAreBounded()
    {
        var session = new RenderTimingSession(1, 2); session.Start();
        var parent = session.Begin(0, 0); var child = session.Begin(0, 1);
        var overflow = session.Begin(0, 2); session.End(overflow, 3);
        session.End(child, 5); session.End(parent, 10);
        Assert.That(session.Snapshot()[0].Calls, Is.EqualTo(2));
        Assert.That(session.Snapshot()[0].Exclusive, Is.EqualTo(10));
        Assert.That(session.Dropped, Is.EqualTo(1));
    }
    [Test]
    public void OldTokensCannotContaminateNewCapture()
    {
        var session = new RenderTimingSession(1); session.Start();
        var old = session.Begin(0, 0); session.Stop(); session.Start();
        var current = session.Begin(0, 20); session.End(old, 25); session.End(current, 30);
        Assert.That(session.Snapshot()[0].Inclusive, Is.EqualTo(10));
        Assert.That(session.Snapshot()[0].Calls, Is.EqualTo(1));
    }
    [Test]
    public void StopIgnoresOpenScopesAndResetsOnRestart()
    {
        var session = new RenderTimingSession(1); session.Start();
        var token = session.Begin(0, 0); session.Stop(); session.End(token, 10);
        Assert.That(session.Snapshot()[0].Calls, Is.Zero);
        session.Start(); var next = session.Begin(0, 20); session.End(next, 30);
        Assert.That(session.Snapshot()[0].Calls, Is.EqualTo(1));
    }
    [Test]
    public void BadNestingRecoversWithoutEscapingIntoGame()
    {
        var session = new RenderTimingSession(2); session.Start();
        var parent = session.Begin(0, 0); session.Begin(1, 1); session.End(parent, 10);
        var next = session.Begin(0, 20); session.End(next, 30);
        Assert.That(session.Dropped, Is.EqualTo(1));
        Assert.That(session.Snapshot()[0].Inclusive, Is.EqualTo(10));
    }
}
