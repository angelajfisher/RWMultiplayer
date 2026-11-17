using Multiplayer.Common;
namespace Tests;
public class TickTimingCounterTest
{
    [Test] public void NormalizesByTicksRatherThanFrames()
    {
        var value = new TickTimingCounter(); value.Add(10, 11, 8, false); value.Add(11, 14, 24, false);
        Assert.That(value.AdvancedTicks, Is.EqualTo(4)); Assert.That(value.TimePerTick, Is.EqualTo(8));
        Assert.That(TickTimingCounter.Rate(10, 130, 2), Is.EqualTo(60));
    }
    [Test] public void PausedAndResetCountersAreNotMisreported()
    {
        var value = new TickTimingCounter(); value.Add(4, 4, 2, false);
        Assert.That(value.TimePerTick, Is.Null); Assert.That(value.NoAdvance, Is.EqualTo(1));
        Assert.That(TickTimingCounter.Rate(4, 4, 2), Is.Zero);
        value.Add(4, 5, 3, false); value.Add(5, 1, 2, true);
        Assert.That(value.Resets, Is.EqualTo(1)); Assert.That(value.Exceptions, Is.EqualTo(1));
        Assert.That(value.TimePerTick, Is.Null); Assert.That(TickTimingCounter.Rate(5, 1, 2), Is.Null);
        Assert.That(TickTimingCounter.Rate(0, 1, 0), Is.Null);
    }
}
