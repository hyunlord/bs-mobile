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
    [TestCase(1,0,"E"),TestCase(1,1,"NE"),TestCase(0,1,"N"),TestCase(-1,1,"NW"),TestCase(-1,0,"W"),TestCase(-1,-1,"SW"),TestCase(0,-1,"S"),TestCase(1,-1,"SE")]
    public void AuthoredDirectionUsesActualVector(int x,int y,string expected)
        => Assert.That(WorldRenderer.ProductionDirection(new UnityEngine.Vector2(x,y)),Is.EqualTo(expected));
    [Test] public void AuthoredEightDirectionsRejectMirroringAndMissingAction()
    {
        var value=ValidManifest();var clips=new System.Collections.Generic.List<HeroProductionClip>();
        foreach(var direction in new[]{"E","NE","N","NW","W","SW","S","SE"})
            foreach(var action in new[]{"idle","walk","slash-light","hit","death"})
            {var c=ValidManifest().clips[0];c.id=action;c.direction=direction;c.authoredDirection=direction;clips.Add(c);}
        value.clips=clips.ToArray();Assert.DoesNotThrow(()=>WorldRenderer.ValidateProductionManifest(value));
        value.clips[0].mirror=true;Assert.Throws<InvalidDataException>(()=>WorldRenderer.ValidateProductionManifest(value));
        value.clips[0].mirror=false;clips.RemoveAt(0);value.clips=clips.ToArray();Assert.Throws<InvalidDataException>(()=>WorldRenderer.ValidateProductionManifest(value));
    }
    [Test] public void ActualCombatAcceptsProductionWithoutIllegalKnightFlag()
    {
        var sidecar=manifest+".case.json";
        File.WriteAllText(sidecar,"{\"contract\":\"m1-combat-case-v1\",\"classification\":\"hypothesis\",\"proposedContract\":\"m1-review-composition-v1\",\"captureStartTick\":300,\"endTick\":600,\"replaySha256\":\"sha\",\"catalogHash\":\"derived\",\"baselineCatalogHash\":\"base\",\"gear\":{},\"arena\":{}}");
        try {var result=M1HeroProductionReview.Parse(new[]{"--wave-benchmark","case.ssreplay","--m1-combat-case",sidecar,"--hero-production",manifest});Assert.That(result.Direction,Is.EqualTo("follow"));}
        finally {File.Delete(sidecar);}
    }
    [Test] public void TerminalTailRequiresOfflineAndIsExplicitlyOneSecond()
    {
        var args=new System.Collections.Generic.List<string>(Args("follow")){"--hero-production-terminal-tail"};
        Assert.Throws<ArgumentException>(()=>M1HeroProductionReview.Parse(args.ToArray()));
        args.Add("--offline-replay-capture");Assert.That(M1HeroProductionReview.Parse(args.ToArray()).TerminalTailFrames,Is.EqualTo(60));
    }
    [Test] public void ActualCombatCannotSubstituteLegacyMirroredOr3dManifest()
        => Assert.Throws<InvalidDataException>(()=>WorldRenderer.ValidateProductionManifest(ValidManifest(),true));
    [Test] public void UnrelatedWeaponsCannotRestartActualMeleeSlashOrOverwriteEvolution()
    {
        var go=new UnityEngine.GameObject("production-event-test");var world=go.AddComponent<WorldRenderer>();
        try
        {
            world.EditorPrepareProductionAttackTest(ValidManifest());
            SowSiege.Core.WaveEvent E(long id,int tick,string kind,string source)=>new SowSiege.Core.WaveEvent(id,tick,kind,source,-1,new SowSiege.Core.WorldPoint(10,20),new SowSiege.Core.WorldPoint(-90,120),0);
            world.EditorAcceptProductionAttack(E(1,30,"attack","core:sowing_sworddance"),1);
            world.EditorAcceptProductionAttack(E(2,30,"attack","core:iron_blade"),1.1f);
            world.EditorAcceptProductionAttack(E(3,31,"chain-link","core:iron_blade"),1.2f);
            world.EditorAcceptProductionAttack(E(4,32,"orbit-fragment","core:ward_orbit"),1.3f);
            world.EditorAcceptProductionAttack(E(5,33,"attack","core:muster_horn"),1.4f);
            world.EditorAcceptProductionAttack(E(6,34,"projectile-launched","core:iron_blade"),1.5f);
            Assert.That(world.EditorProductionAttackStarted,Is.EqualTo(1));Assert.That(world.EditorProductionAttackEvolved,Is.True);
            Assert.That(world.HeroProductionAttackTick,Is.EqualTo(30));Assert.That(world.HeroProductionSlashDirection,Is.EqualTo("NW"));
            Assert.That(world.HeroProductionTargetDirection,Is.EqualTo(new UnityEngine.Vector2(-100,100)));
            Assert.That(UnityEngine.Mathf.FloorToInt((1.5f-world.EditorProductionAttackStarted)*12),Is.EqualTo(6),"Unrelated events must not pin the slash at frame zero.");
            world.EditorAcceptProductionAttack(E(7,60,"attack","core:iron_blade"),2);
            Assert.That(world.EditorProductionAttackStarted,Is.EqualTo(2));Assert.That(world.EditorProductionAttackEvolved,Is.False);
        }
        finally {UnityEngine.Object.DestroyImmediate(go);}
    }
    static HeroProductionManifest ValidManifest() => new HeroProductionManifest
    {
        schemaVersion = 1, method = "frame-drawn", headHeightRatio = 1f / 7,
        clips = new[] { new HeroProductionClip { id = "walk", direction = "E", texture = "walk.png",
            columns = 4, rows = 2, frames = 8, fps = 8, pivotX = .5f, pivotY = .1f,
            bodyHeightPixels = 100, headHeightPixels = 14 } }
    };
}
