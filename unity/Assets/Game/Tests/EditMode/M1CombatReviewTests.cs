using System;
using System.IO;
using Game.App;
using NUnit.Framework;
using SowSiege.Core;
public sealed class M1CombatReviewTests
{
    string path;
    [SetUp] public void Setup()
    {
        path=Path.GetTempFileName();
        File.WriteAllText(path,"{\"contract\":\"m1-combat-case-v1\",\"classification\":\"hypothesis\",\"proposedContract\":\"m1-review-composition-v1\",\"captureStartTick\":300,\"endTick\":600,\"replaySha256\":\"sha\",\"catalogHash\":\"derived\",\"baselineCatalogHash\":\"base\",\"gear\":{},\"arena\":{}}");
    }
    [TearDown] public void Cleanup()=>File.Delete(path);
    string[] Args(params string[] extra)
    { var basic=new[]{"--m1-combat-case",path,"--wave-benchmark","fixture.ssreplay"};var result=new string[basic.Length+extra.Length];basic.CopyTo(result,0);extra.CopyTo(result,basic.Length);return result; }
    [Test] public void ReviewArenaRadiusDeserializesAndCannotDisagreeWithReplay()
    {
        File.WriteAllText(path,File.ReadAllText(path).Replace("\"arena\":{}","\"arena\":{\"spawnRadius\":1200,\"recycleRadius\":3000,\"enemyBodyWidth\":475}"));
        var review=M1CombatReview.Parse(Args());
        Assert.That(review.Definition.arena.enemyBodyWidth,Is.EqualTo(475));
        var options=new InteractiveOptions(new RunOptions(1,"hero","estate","mixed"),AimMode.Movement,"hash",Field:new FieldOptions(FieldOptions.ReviewVersion,1200,3000),MeleeAim:new MeleeAimOptions());
        Assert.DoesNotThrow(()=>review.ValidateReplayOptions(options));
        review.Definition.arena.spawnRadius++;
        Assert.Throws<InvalidDataException>(()=>review.ValidateReplayOptions(options));
    }
    [Test] public void OrdinaryGameHasNoCase()=>Assert.That(M1CombatReview.Parse(Array.Empty<string>()),Is.Null);
    [Test] public void ExplicitCaseCapturesLastTenSeconds()
    { var window=CameraScaleComparison.Parse(Args());Assert.That(window.StartTick,Is.EqualTo(300));Assert.That(window.EndTick,Is.EqualTo(600));Assert.That(OfflineReplayCapture.ExpectedFrames(window.EndTick-window.StartTick,30),Is.EqualTo(600)); }
    [TestCase("--knight-review"),TestCase("--m1-weapon-showcase"),TestCase("--camera-scale")]
    public void MixedContractsRejected(string option)=>Assert.Throws<ArgumentException>(()=>M1CombatReview.Parse(Args(option,"all")));
    [Test] public void SidecarIdentityRecorded()=>Assert.That(M1CombatReview.Parse(Args()).SidecarSha256.Length,Is.EqualTo(64));
    [Test] public void UnknownContractRejected()
    {File.WriteAllText(path,File.ReadAllText(path).Replace("m1-combat-case-v1","unknown"));Assert.Throws<InvalidDataException>(()=>M1CombatReview.Parse(Args()));}
    static WaveEvent Event(long id,int tick,string kind,string source,int amount=5)=>new WaveEvent(id,tick,kind,source,42,new WorldPoint(0,0),new WorldPoint(0,0),amount);
    string Save(M1CombatReview review)
    {
        var output=path+"-output";Directory.CreateDirectory(output);
        try { review.Save(output,true);return File.ReadAllText(Path.Combine(output,"m1-combat-review.json"))+"\n"+File.ReadAllText(Path.Combine(output,"m1-combat-events.csv")); }
        finally {Directory.Delete(output,true);}
    }
    [TestCase("hit"),TestCase("group-hit"),TestCase("trained-group-hit")]
    public void SelectedDamageAttributesSameTickDeathAndFirstVisibleFrame(string kind)
    {
        var review=M1CombatReview.Parse(Args());review.Definition.id="core:muster_horn";
        review.EditorRecordEvent(7,Event(1,300,kind,"core:muster_horn"),true);
        review.EditorRecordEvent(7,Event(2,300,"enemy-killed","core:raider"),true);
        var output=Save(review);
        Assert.That(output,Does.Contain("\"firstVisibleHitFrame\": 7"));
        Assert.That(output,Does.Contain("\"firstVisibleDeathFrame\": 7"));
        Assert.That(output,Does.Contain("enemy-killed,core:raider"));
    }
    [TestCase("core:raider",5,300),TestCase("core:muster_horn",0,300),TestCase("core:muster_horn",5,301)]
    public void EnemySourceZeroDamageAndDifferentTickCannotAttributeDeath(string source,int amount,int deathTick)
    {
        var review=M1CombatReview.Parse(Args());review.Definition.id="core:muster_horn";
        review.EditorRecordEvent(0,Event(1,300,"group-hit",source,amount),true);
        review.EditorRecordEvent(2,Event(2,deathTick,"enemy-killed","core:raider"),true);
        var output=Save(review);Assert.That(output,Does.Contain("\"firstVisibleDeathFrame\": -1"));
        if(source=="core:raider"||amount==0)Assert.That(output,Does.Contain("\"firstVisibleHitFrame\": -1"));
        Assert.That(output,Does.Not.Contain("enemy-killed,core:raider"));
    }
    [Test] public void WindowMatchesRawEventTicks300Through599()
    {
        var review=M1CombatReview.Parse(Args());review.Definition.id="core:iron_blade";
        review.EditorRecordEvent(0,Event(1,299,"hit","core:iron_blade"),true);
        review.EditorRecordEvent(1,Event(2,300,"hit","core:iron_blade"),true);
        review.EditorRecordEvent(598,Event(3,599,"hit","core:iron_blade"),true);
        review.EditorRecordEvent(599,Event(4,600,"hit","core:iron_blade"),true);
        var output=Save(review);Assert.That(output,Does.Contain("\"firstVisibleHitFrame\": 1"));
        Assert.That(output,Does.Not.Contain(",299,hit,"));Assert.That(output,Does.Not.Contain(",600,hit,"));
        Assert.That(output,Does.Contain(",300,hit,"));Assert.That(output,Does.Contain(",599,hit,"));
    }
    [Test] public void ActualFixtureSidecarsMatchFrozenDerivedCatalogHashes()
    {
        var root=Environment.GetEnvironmentVariable("M1_COMBAT_FIXTURES")??Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../../.omo/evidence/m1-256-combat-candidate4"));
        if(!Directory.Exists(root))Assert.Ignore("Generated actual fixture sidecars unavailable; run showcase-fixtures first.");
        var baseline=RunCoordinator.EditorFrozenWaveBenchmark(out var baselineDataHash);
        Assert.That(baseline,Is.Not.Null,"Frozen C05 catalog must be prepared.");
        var files=Directory.GetFiles(root,"*.case.json");Assert.That(files.Length,Is.EqualTo(24));
        var failures=new System.Collections.Generic.List<string>();
        foreach(var file in files)
        {
            var review=M1CombatReview.Parse(new[]{"--m1-combat-case",file,"--wave-benchmark","bound.ssreplay"});
            var c=review.Definition.gear.composition;
            TestContext.WriteLine(Path.GetFileName(file)+": composition="+(c==null?"null":c.version+"/"+c.placement+"/"+c.radius));
            try {var derived=review.Apply(baseline,baselineDataHash,review.Definition.replaySha256);Assert.That(ReviewShowcaseCatalog.Hash(derived),Is.EqualTo(review.Definition.catalogHash));}
            catch(Exception e){failures.Add(Path.GetFileName(file)+": "+e.Message);}
        }
        Assert.That(failures,Is.Empty,string.Join("\n",failures));
    }
    [Test] public void ExplicitNullCompositionRemainsAbsentAfterUnityDeserialization()
    {
        File.WriteAllText(path,File.ReadAllText(path).Replace("\"gear\":{}","\"gear\":{\"composition\":null}"));
        Assert.That(M1CombatReview.Parse(Args()).Definition.gear.composition,Is.Null);
    }
    [Test] public void MalformedNonemptyCompositionIsNotSilentlyDropped()
    {
        File.WriteAllText(path,File.ReadAllText(path).Replace("\"gear\":{}","\"gear\":{\"composition\":{\"radius\":1}}"));
        Assert.Throws<InvalidDataException>(()=>M1CombatReview.Parse(Args()));
    }
    [Test] public void ValidCompositionRetainsItsParameters()
    {
        File.WriteAllText(path,File.ReadAllText(path).Replace("\"gear\":{}","\"gear\":{\"composition\":{\"version\":1,\"placement\":\"nearest-target\",\"delayTicks\":15,\"radius\":500}}"));
        var composition=M1CombatReview.Parse(Args()).Definition.gear.composition;
        Assert.That(composition.radius,Is.EqualTo(500));Assert.That(composition.delayTicks,Is.EqualTo(15));
    }
    void WriteCombined()
    {
        var value=new M1CombatReview.Case {contract="m1-combat-combined-v1",captureStartTick=900,endTick=1500,replaySha256="sha",catalogHash="derived",baselineCatalogHash="base",
            combined=new M1CombatReview.Combined {captureStartTick=900,lordHealth=1000,weaponSlots=7,toolSlots=1,arena=new M1CombatReview.Arena(),cases=new M1CombatReview.CombinedEntry[8]},sources=new M1CombatReview.Source[8]};
        for(var i=0;i<8;i++)
        {
            var id="test:source"+i;
            value.combined.cases[i]=new M1CombatReview.CombinedEntry {id=id,stage="evolved",classification="hypothesis",proposedContract="m1-review-composition-v1",gear=new M1CombatReview.Gear(),arena=new M1CombatReview.Arena()};
            value.sources[i]=new M1CombatReview.Source {id=id,shape="sweep",audioCue="blade"};
        }
        File.WriteAllText(path,UnityEngine.JsonUtility.ToJson(value));
    }
    [Test] public void CombinedWindowAndEverySourceAreBound()
    {
        WriteCombined();var review=M1CombatReview.Parse(Args());
        Assert.That(review.IsCombined,Is.True);Assert.That(review.CueFor("test:source7"),Is.EqualTo("blade"));Assert.That(review.CueFor("other"),Is.Null);
        var window=CameraScaleComparison.Parse(Args());Assert.That(window.StartTick,Is.EqualTo(900));Assert.That(window.EndTick,Is.EqualTo(1500));
    }
    [TestCase("--offline-replay-capture"),TestCase("--wave-benchmark-capture")]
    public void CombinedStressRejectsCapture(string flag)
    {WriteCombined();Assert.Throws<ArgumentException>(()=>M1CombatReview.Parse(Args(flag)));}
    [Test] public void CombinedActualActivationCountersDeduplicateAndRespectWindow()
    {
        WriteCombined();var review=M1CombatReview.Parse(Args());
        review.EditorObserveCombinedEvent(Event(1,899,"attack","test:source0"));
        for(var i=0;i<8;i++) {var e=Event(i+2,900,"attack","test:source"+i);review.EditorObserveCombinedEvent(e);review.EditorObserveCombinedEvent(e);}
        review.EditorObserveCombinedEvent(Event(10,1500,"attack","test:source0"));
        var output=path+"-combined";Directory.CreateDirectory(output);
        try {review.Save(output,true);var json=File.ReadAllText(Path.Combine(output,"m1-combat-review.json"));Assert.That(json,Does.Contain("\"allEightSourcesActivated\": false"));Assert.That(json.Split(new[]{"\"activationEvents\": 1"},StringSplitOptions.None).Length-1,Is.EqualTo(8));}
        finally {Directory.Delete(output,true);}
    }
    [Test] public void CombinedSourcesCannotAliasOneAnother()
    {WriteCombined();File.WriteAllText(path,File.ReadAllText(path).Replace("test:source7","test:source0"));Assert.Throws<InvalidDataException>(()=>M1CombatReview.Parse(Args()));}
    [Test] public void CombinedActivationProofRequiresAuthoritativeWindowCounterDeltas()
    {
        WriteCombined();var review=M1CombatReview.Parse(Args());var counters=new System.Collections.Generic.Dictionary<string,long>();
        for(var i=0;i<8;i++) counters["activation-started:test:source"+i]=10;
        review.EditorObserveCombinedCounters(counters,900);
        for(var i=0;i<8;i++) counters["activation-started:test:source"+i]=11;
        review.EditorObserveCombinedCounters(counters,1500);
        var output=path+"-counter-proof";Directory.CreateDirectory(output);
        try {review.Save(output,true);var json=File.ReadAllText(Path.Combine(output,"m1-combat-review.json"));Assert.That(json,Does.Contain("\"allEightSourcesActivated\": true"));Assert.That(json.Split(new[]{"\"activations\": 1"},StringSplitOptions.None).Length-1,Is.EqualTo(8));}
        finally {Directory.Delete(output,true);}
    }
    [Test] public void CombinedSecondaryEnemyFieldsSurviveUnityDeserialization()
    {
        WriteCombined();var value=UnityEngine.JsonUtility.FromJson<M1CombatReview.Case>(File.ReadAllText(path));
        value.combined.arena.enemyId="test:durable";value.combined.arena.durationTicks=1500;
        value.combined.secondaryEnemyId="test:fragile";value.combined.secondaryEnemyHealth=100;value.combined.secondaryEnemyFirstSpawnTick=850;
        File.WriteAllText(path,UnityEngine.JsonUtility.ToJson(value));
        var parsed=M1CombatReview.Parse(Args()).Definition.combined;
        Assert.That(parsed.secondaryEnemyId,Is.EqualTo("test:fragile"));Assert.That(parsed.secondaryEnemyHealth,Is.EqualTo(100));Assert.That(parsed.secondaryEnemyFirstSpawnTick,Is.EqualTo(850));
        value.combined.secondaryEnemyHealth=0;File.WriteAllText(path,UnityEngine.JsonUtility.ToJson(value));Assert.Throws<InvalidDataException>(()=>M1CombatReview.Parse(Args()));
    }
    [Test] public void CombinedDeathAndRewardCountersUseActualWindowEventsExactlyOnce()
    {
        WriteCombined();var review=M1CombatReview.Parse(Args());
        review.EditorObserveCombinedEvent(Event(1,899,"enemy-killed","test:enemy",20));
        var death=Event(2,900,"enemy-killed","test:enemy",7);review.EditorObserveCombinedEvent(death);review.EditorObserveCombinedEvent(death);
        review.EditorObserveCombinedEvent(Event(3,1499,"reward-created","test:source0",11));
        review.EditorObserveCombinedEvent(Event(4,1500,"reward-collected","test:source0",11));
        var output=path+"-reward-proof";Directory.CreateDirectory(output);
        try {review.Save(output,true);var json=File.ReadAllText(Path.Combine(output,"m1-combat-review.json"));
            Assert.That(json,Does.Contain("\"enemyKilledEvents\": 1"));Assert.That(json,Does.Contain("\"killExperienceAwarded\": 7"));
            Assert.That(json,Does.Contain("\"rewardCreatedEvents\": 1"));Assert.That(json,Does.Contain("\"rewardExperienceCreated\": 11"));
            Assert.That(json,Does.Contain("\"rewardCollectedEvents\": 0"));Assert.That(json,Does.Contain("\"rewardExperienceCollected\": 0"));}
        finally {Directory.Delete(output,true);}
    }
}
