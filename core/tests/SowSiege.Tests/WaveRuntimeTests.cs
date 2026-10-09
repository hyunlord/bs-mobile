using SowSiege.Core;
using Xunit;
namespace SowSiege.Tests;

public sealed class WaveRuntimeTests
{
    internal static (ContentCatalog Catalog, WaveRuntimeSystem System, WorldState World, EnemyState Enemy) Arena(WaveAttackKind kind)
    {
        var c=FirstPlayableTests.Catalog("arc");var weapon=c.Weapons.Keys.First();var enemyId=c.Enemies.Keys.First();
        var gear=new WaveGearDefinition(weapon,"design:test",kind,10,200,10,10,3,100,0,10,100,3,10);
        c=c with {Runtime=null,FirstPlayable=null,WeaponCombat=null,WaveRuntime=new("test","test:chapter","design:test",enemyId,10000,2,2,5,5,100,20,30,2,40,20,new Dictionary<string,WaveGearDefinition>{{weapon,gear}},new Dictionary<string,WaveItemDefinition>(),new Dictionary<string,WaveEvolutionDefinition>(),new Dictionary<string,WaveEnemyDefinition>{{enemyId,new(enemyId,"design:test",WaveEnemyKind.Pursuer,30,10,30,10,10000)}}),Enemies=new Dictionary<string,EnemyDefinition>{{enemyId,new(enemyId,"lord",100,0,1,1,100,1)}}};
        var w=new WorldState{Lord=new(500,500),LordHealth=100};w.Equipment.Add(new(){Id=weapon});var sys=new WaveRuntimeSystem(c,w,new(1,true),null);var enemy=new EnemyState{Id=10,Definition=enemyId,Position=new(600,500),Health=100};w.Enemies.Add(enemy);return(c,sys,w,enemy);
    }
    [Fact] public void FacingArcDoesNotHitBehindLord()
    {var (_,s,w,e)=Arena(WaveAttackKind.Arc);e.Position=new(400,500);s.Tick(w.Lord);Assert.Equal(100,e.Health);e.Position=new(600,500);w.Tick=10;s.Tick(w.Lord);Assert.Equal(90,e.Health);}
    [Fact] public void HomingProjectileTravelsAndExpiresWhenLockedTargetDies()
    {var (_,s,w,e)=Arena(WaveAttackKind.Homing);s.Tick(w.Lord);Assert.Equal(100,e.Health);Assert.Single(w.WaveRuntime!.Projectiles);e.Health=0;w.Tick++;s.Tick(w.Lord);Assert.Empty(w.WaveRuntime.Projectiles);}
    [Fact] public void ChainNeverHitsSameTargetTwice()
    {var (_,s,w,e)=Arena(WaveAttackKind.Chain);s.Tick(w.Lord);Assert.Equal(90,e.Health);Assert.Single(w.WaveRuntime!.Events,x=>x.Kind=="chain-link");}
    [Fact] public void OrbitLeavesCenterEmpty()
    {var (_,s,w,e)=Arena(WaveAttackKind.Orbit);e.Position=w.Lord;s.Tick(w.Lord);Assert.Equal(100,e.Health);Assert.Equal(3,w.WaveRuntime!.Attacks.Count);}
    [Fact] public void MaterialTargetReplayRoundTripsWithoutChangingLegacyFormat()
    {
        var run=new RunOptions(1,"test:hero","test:estate","random",ManualCards:true);
        var original=new InteractiveOptions(run,AimMode.Movement,new string('A',64));
        Assert.Equal(1,ReplayCodec.Header(original).FormatVersion);
        var target=original with {Run=run with {TargetMaterial="test:grain"}};
        using var stream=new MemoryStream();ReplayCodec.WriteHeader(stream,ReplayCodec.Header(target));ReplayCodec.WriteEnd(stream,new(0,0,ReplayEndKind.Quit,new string('A',64)));stream.Position=0;
        Assert.Equal("test:grain",ReplayCodec.Read(stream).Header.Options.Run.TargetMaterial);
    }
    [Fact] public void InvalidMaterialFailsBeforeSimulation()
    {var (c,_,_,_)=Arena(WaveAttackKind.Arc);Assert.Throws<ArgumentException>(()=>new Simulation(c,new(1,c.Tuning.DefaultHero,c.Tuning.DefaultEstate,"random",TargetMaterial:"missing:target"),new TestHasher()));}
    private sealed class TestHasher:IStateHasher {public string Compute(object value)=>"test";}
}
