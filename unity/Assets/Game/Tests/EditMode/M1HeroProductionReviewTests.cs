using System;
using System.IO;
using Game.App;
using Game.View;
using NUnit.Framework;

public sealed class M1HeroProductionReviewTests
{
    string manifest;
    [SetUp] public void SetUp() { manifest = Path.Combine(Path.GetTempPath(),Guid.NewGuid() + ".json"); File.WriteAllText(manifest,"{}"); }
    [TearDown] public void TearDown() => File.Delete(manifest);
    string[] Args(string direction) => new[] { "--knight-review","--wave-benchmark","same.ssreplay","--hero-production",manifest,"--hero-production-direction",direction };
    [Test] public void OrdinaryExecutionKeepsExistingHero() => Assert.That(M1HeroProductionReview.Parse(Array.Empty<string>()),Is.Null);
    [Test] public void ExplicitProductionPinsManifestAndDirection()
    {
        var value = M1HeroProductionReview.Parse(Args("E"));
        Assert.That(value.Direction,Is.EqualTo("E")); Assert.That(value.ManifestPath,Is.EqualTo(manifest));
        Assert.That(value.ManifestSha256.Length,Is.EqualTo(64));
    }
    [Test] public void DirectionRequiresProductionManifest() => Assert.Throws<ArgumentException>(()=>M1HeroProductionReview.Parse(new[] { "--hero-production-direction","E" }));
    [Test] public void ProductionRequiresKnightContract() => Assert.Throws<ArgumentException>(()=>M1HeroProductionReview.Parse(new[] { "--hero-production",manifest }));
    [Test] public void UnknownDirectionRejected() => Assert.Throws<ArgumentException>(()=>M1HeroProductionReview.Parse(Args("up")));
    [Test] public void FollowUsesTheSameFacingAsTheBaseline() => Assert.That(M1HeroProductionReview.Parse(Args("follow")).Direction, Is.EqualTo("follow"));
    [Test] public void DuplicateDirectionRejected()
    {
        var args = new System.Collections.Generic.List<string>(Args("E")) { "--hero-production-direction","W" };
        Assert.Throws<ArgumentException>(()=>M1HeroProductionReview.Parse(args.ToArray()));
    }
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(0f)]
    public void InvalidFrameTimingRejectedBeforeAtlasLoad(float fps)
    {
        var value = ValidManifest(); value.clips[0].fps = fps;
        Assert.Throws<InvalidDataException>(() => WorldRenderer.ValidateProductionManifest(value));
    }
    [Test] public void InvalidMeasuredHeadCannotPretendToBeAValidBody()
    {
        var value = ValidManifest(); value.clips[0].headHeightPixels = 120;
        Assert.Throws<InvalidDataException>(() => WorldRenderer.ValidateProductionManifest(value));
    }
    static HeroProductionManifest ValidManifest() => new HeroProductionManifest
    {
        schemaVersion = 1, method = "frame-drawn", headHeightRatio = 1f / 7,
        clips = new[] { new HeroProductionClip { id = "walk", direction = "E", texture = "walk.png",
            columns = 4, rows = 2, frames = 8, fps = 8, pivotX = .5f, pivotY = .1f,
            bodyHeightPixels = 100, headHeightPixels = 14 } }
    };
}
