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
    [Test] public void ExplicitSeedAndTopologyDoNotChangeUninstrumentedLaunch()
    {
        Assert.That(NormalGcProbe.ParseSeed(Array.Empty<string>()), Is.Null);
        Assert.That(NormalGcProbe.ParseTopology(Array.Empty<string>()), Is.Null);
        Assert.That(NormalGcProbe.ParseSeed(new[] { "--normal-gc-probe" }), Is.EqualTo(30001));
        Assert.That(NormalGcProbe.ParseTopology(new[] { "--normal-gc-probe" }), Is.EqualTo("old"));
        Assert.That(NormalGcProbe.ParseSeed(new[] { "--normal-gc-probe", "--normal-gc-seed", "42" }), Is.EqualTo(42));
        Assert.That(NormalGcProbe.ParseTopology(new[] { "--normal-gc-probe", "--normal-gc-topology", "field" }), Is.EqualTo("field"));
    }
    [Test] public void RepeatedOrInvalidExperimentConfigurationIsRejected()
    {
        Assert.Throws<ArgumentException>(() => NormalGcProbe.ParseSeed(new[] { "--normal-gc-probe", "--normal-gc-seed", "-1" }));
        Assert.Throws<ArgumentException>(() => NormalGcProbe.ParseSeed(new[] { "--normal-gc-probe", "--normal-gc-seed", "2", "--normal-gc-seed", "3" }));
        Assert.Throws<ArgumentException>(() => NormalGcProbe.ParseTopology(new[] { "--normal-gc-probe", "--normal-gc-topology", "other" }));
        Assert.Throws<ArgumentException>(() => NormalGcProbe.Requested(new[] { "--normal-gc-probe", "--feel-trial", "all" }));
        Assert.Throws<ArgumentException>(() => NormalGcProbe.Requested(new[] { "--normal-gc-probe", "--wave-benchmark", "replay" }));
    }
}
