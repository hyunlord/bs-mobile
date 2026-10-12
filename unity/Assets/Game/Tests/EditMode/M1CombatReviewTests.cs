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
}
