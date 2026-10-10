using System;
using Game.App;
using NUnit.Framework;

public sealed class NormalGcProbeTests
{
    [Test] public void MustBeExplicitAndCannotContaminateCapture()
    {
        Assert.That(NormalGcProbe.Requested(Array.Empty<string>()), Is.False);
        Assert.That(NormalGcProbe.Requested(new[] { "--normal-gc-probe" }), Is.True);
        Assert.Throws<ArgumentException>(() => NormalGcProbe.Requested(new[] { "--normal-gc-probe", "--autoplay-capture" }));
        Assert.Throws<ArgumentException>(() => NormalGcProbe.Requested(new[] { "--normal-gc-probe", "--smoothness-trace" }));
    }
    [TestCase(0,0,0)] [TestCase(5,5,0)] [TestCase(5,8,3)]
    public void RecordsEveryCollectionIncrement(int previous,int current,int expected) => Assert.That(NormalGcProbe.CollectionDelta(previous,current),Is.EqualTo(expected));
    [Test] public void CounterRegressionIsNotReportedAsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NormalGcProbe.CollectionDelta(8,7));
        Assert.Throws<ArgumentOutOfRangeException>(() => NormalGcProbe.CollectionDelta(-1,0));
    }
    [Test] public void CompletionRequiresSixGameplayMinutes()
    {
        Assert.That(NormalGcProbe.ReachedSixMinutes(10799,30),Is.False);
        Assert.That(NormalGcProbe.ReachedSixMinutes(10800,30),Is.True);
        Assert.That(NormalGcProbe.ReachedSixMinutes(10800,0),Is.False);
    }
}
