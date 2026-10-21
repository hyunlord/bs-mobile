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
    [Test] public void UnavailableTextureProfilerDoesNotReportZeroMemory()
    {
        var go=new UnityEngine.GameObject("production-memory-test");var world=go.AddComponent<WorldRenderer>();
        try
        {
            world.EditorRecordProductionTextureMemory(1024,1024,4194304);
            Assert.That(world.HeroProductionTextureBytesAvailable,Is.True);
            Assert.That(world.HeroProductionTextureBytes,Is.EqualTo(4194304));
            world.EditorRecordProductionTextureMemory(512,512,0);
            Assert.That(world.HeroProductionTextureBytesAvailable,Is.False);
            Assert.That(world.HeroProductionTextureBytes,Is.EqualTo(-1));
            Assert.That(world.HeroProductionNominalRgbaBytes,Is.EqualTo(5242880));
            world.EditorRecordProductionTextureMemory(512,512,1048576);
            Assert.That(world.HeroProductionTextureBytesAvailable,Is.False,"A later measurable atlas cannot repair an incomplete total.");
            Assert.That(world.HeroProductionTextureBytes,Is.EqualTo(-1));
        }
        finally {UnityEngine.Object.DestroyImmediate(go);}
    }
    [TestCase(-1),TestCase(8)] public void InvalidImpactFrameIsRejected(int impactFrame)
    {
        var value=ValidManifest();value.clips[0].impactFrame=impactFrame;
        Assert.Throws<InvalidDataException>(()=>WorldRenderer.ValidateProductionManifest(value));
    }
    [Test] public void ActualImpactStartsAtContactAndFinishesRecoveryWithoutWindupDelay()
    {
        var clip=ValidManifest().clips[0];clip.id="slash-light";clip.fps=12;clip.impactFrame=3;
        Assert.That(WorldRenderer.ProductionFrame(clip.id,WorldRenderer.ProductionAttackTime(clip,0),clip.frames,clip.fps),Is.EqualTo(3));
        Assert.That(WorldRenderer.ProductionFrame(clip.id,WorldRenderer.ProductionAttackTime(clip,1f/12),clip.frames,clip.fps),Is.EqualTo(4));
        Assert.That(WorldRenderer.ProductionAttackDuration(clip),Is.EqualTo(5f/12));
        Assert.That(WorldRenderer.ProductionFrame(clip.id,WorldRenderer.ProductionAttackTime(clip,1),clip.frames,clip.fps),Is.EqualTo(7));
        clip.impactFrame=0;
        Assert.That(WorldRenderer.ProductionAttackTime(clip,0),Is.Zero,"Historical clips keep their original start.");
        Assert.That(WorldRenderer.ProductionAttackDuration(clip),Is.EqualTo(8f/12));
    }
    [Test] public void AttackLifetimeUsesTargetDirectionClipInsteadOfWalkingDirection()
    {
        var go=new UnityEngine.GameObject("directional-attack-duration-test");var world=go.AddComponent<WorldRenderer>();
        try
        {
            var east=ValidManifest().clips[0];east.id="slash-light";east.fps=24;east.impactFrame=6;
            var northwest=ValidManifest().clips[0];northwest.id="slash-light";northwest.direction="NW";northwest.fps=8;northwest.impactFrame=2;
            var manifest=ValidManifest();manifest.clips=new[]{east,northwest};world.EditorPrepareProductionAttackTest(manifest);
            world.EditorAcceptProductionAttack(new SowSiege.Core.WaveEvent(1,30,"attack","core:iron_blade",-1,
                new SowSiege.Core.WorldPoint(0,0),new SowSiege.Core.WorldPoint(-100,100),0),1);
            Assert.That(world.EditorResolveProductionAttack("E",.5f,out var clip),Is.True,"East walking clip expires after 2/24s, but the actual NW attack lasts 6/8s.");
            Assert.That(clip,Is.SameAs(northwest));Assert.That(world.HeroProductionDirection,Is.EqualTo("NW"));
            Assert.That(WorldRenderer.ProductionFrame(clip.id,WorldRenderer.ProductionAttackTime(clip,.5f),clip.frames,clip.fps),Is.EqualTo(6));
            Assert.That(world.EditorResolveProductionAttack("E",.75f,out clip),Is.False);
            Assert.That(world.HeroProductionDirection,Is.EqualTo("E"),"Finished attack returns to movement direction.");
            east.fps=1;east.impactFrame=0;
            Assert.That(world.EditorResolveProductionAttack("E",.8f,out clip),Is.False,"A longer walking-direction clip cannot extend the actual attack.");
        }
        finally {UnityEngine.Object.DestroyImmediate(go);}
    }
    [Test] public void OccludedHeadRequiresExplicitUnavailableFlagAndKeepsRatioUnavailable()
    {
        var value=ValidManifest();
        const string json="{\"schemaVersion\":1,\"method\":\"frame-drawn\",\"clips\":[{\"id\":\"walk\",\"direction\":\"N\",\"texture\":\"walk.png\",\"columns\":4,\"rows\":2,\"frames\":8,\"fps\":8,\"pivotX\":0.5,\"pivotY\":0.1,\"bodyHeightPixels\":100,\"headHeightPixels\":null,\"headMeasurementUnavailable\":true}]}";
        value=UnityEngine.JsonUtility.FromJson<HeroProductionManifest>(json);
        Assert.That(value.clips[0].headHeightPixels,Is.Zero);
        Assert.DoesNotThrow(()=>WorldRenderer.ValidateProductionManifest(value));
        Assert.That(value.clips[0].MeasuredHeadHeightRatio,Is.Null);
        value.clips[0].headMeasurementUnavailable=false;
        Assert.Throws<InvalidDataException>(()=>WorldRenderer.ValidateProductionManifest(value));
    }
    [TestCase(-1f),TestCase(14f),TestCase(float.NaN),TestCase(float.PositiveInfinity)]
    public void UnavailableHeadCannotConcealInvalidOrConflictingMeasurement(float pixels)
    {
        var value=ValidManifest();value.clips[0].headMeasurementUnavailable=true;value.clips[0].headHeightPixels=pixels;
        Assert.Throws<InvalidDataException>(()=>WorldRenderer.ValidateProductionManifest(value));
    }
    [Test] public void HistoricalMeasuredHeadRetainsItsMeasuredRatio()
    {
        var value=ValidManifest();Assert.DoesNotThrow(()=>WorldRenderer.ValidateProductionManifest(value));
        Assert.That(value.clips[0].MeasuredHeadHeightRatio,Is.EqualTo(.14f));
    }
    static HeroProductionManifest ValidManifest() => new HeroProductionManifest
    {
        schemaVersion = 1, method = "frame-drawn", headHeightRatio = 1f / 7,
        clips = new[] { new HeroProductionClip { id = "walk", direction = "E", texture = "walk.png",
            columns = 4, rows = 2, frames = 8, fps = 8, pivotX = .5f, pivotY = .1f,
            bodyHeightPixels = 100, headHeightPixels = 14 } }
    };
}
