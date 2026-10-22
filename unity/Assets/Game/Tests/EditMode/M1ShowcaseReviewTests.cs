using System;
using System.IO;
using Game.App;
using NUnit.Framework;

public sealed class M1ShowcaseReviewTests
{
    string catalog;
    [SetUp] public void SetUp()
    {
        catalog = Path.GetTempFileName();
        var entries = new string[8];
        for (var i = 0; i < entries.Length; i++) entries[i] = "{\"id\":\"core:review_" + i + "\",\"shape\":\"shape_" + i + "\",\"name\":\"검토\",\"cue\":\"cue\",\"runtimeSupport\":\"visual only\"}";
        File.WriteAllText(catalog,"{\"contract\":\"m1-weapon-visual-proposals-v1\",\"secondsPerStage\":10,\"entries\":[" + string.Join(",",entries) + "]}");
    }
    [TearDown] public void TearDown() => File.Delete(catalog);
    string[] Args(string stage = "level1") => new[] { "--knight-review","--wave-benchmark","input.ssreplay","--m1-weapon-showcase","core:review_0","--showcase-stage",stage,"--m1-showcase-catalog",catalog };
    [Test] public void OrdinaryGameDoesNotEnableProposal() => Assert.That(M1ShowcaseReview.Parse(Array.Empty<string>()),Is.Null);
    [TestCase("level1",0),TestCase("max",1),TestCase("evolution",2)]
    public void ExplicitReviewKeepsStageVisualAndBounded(string stage,int expected)
    {
        var review = M1ShowcaseReview.Parse(Args(stage));
        Assert.That(review.StageIndex,Is.EqualTo(expected));
        Assert.That(review.EffectSeconds,Is.EqualTo(10));
        Assert.That(review.DisplayLabel,Does.Contain("피해 판정 없음"));
    }
    string[] AllArgs(params string[] extra)
    {
        var args = new[] { "--wave-benchmark", "input.ssreplay", "--m1-weapon-showcase", "all", "--showcase-stage", "evolution", "--m1-showcase-catalog", catalog };
        var result = new string[args.Length + extra.Length]; args.CopyTo(result,0); extra.CopyTo(result,args.Length); return result;
    }
    [Test] public void AllShapesUseNativeBenchmarkWithoutKnightOrCapture()
    {
        var review = M1ShowcaseReview.Parse(AllArgs());
        Assert.That(review.PerformanceOnly,Is.True);
        Assert.That(review.Selected.id,Is.EqualTo("all"));
        Assert.That(CameraScaleComparison.Parse(AllArgs()),Is.Null);
    }
    [TestCase("--offline-replay-capture"),TestCase("--wave-benchmark-capture"),TestCase("--autoplay-capture")]
    public void AllShapesRejectCapture(string flag) => Assert.Throws<ArgumentException>(() => M1ShowcaseReview.Parse(AllArgs(flag,"out")));
    [Test] public void AllShapesRequireReplay()
    {
        var args = AllArgs(); args[0] = "--missing-replay";
        Assert.Throws<ArgumentException>(() => M1ShowcaseReview.Parse(args));
    }
    [Test] public void AllShapesRequireEvolutionLoad()
    {
        var args = AllArgs(); args[5] = "level1";
        Assert.Throws<ArgumentException>(() => M1ShowcaseReview.Parse(args));
    }
    [Test] public void UnknownStageFailsClosed() => Assert.Throws<ArgumentException>(() => M1ShowcaseReview.Parse(Args("production")));
    [Test] public void OrphanStageCannotAffectOrdinaryGameplay() => Assert.Throws<ArgumentException>(() => M1ShowcaseReview.Parse(new[] { "--showcase-stage","max" }));
    [Test] public void RepeatedCatalogIdsAreRejected()
    {
        File.WriteAllText(catalog,File.ReadAllText(catalog).Replace("core:review_1","core:review_0"));
        Assert.Throws<InvalidDataException>(() => M1ShowcaseReview.Parse(Args()));
    }
}
