using System;
using System.Collections.Generic;
using System.IO;
using SowSiege.Core;
using Game.View;
using UnityEngine;

namespace Game.App
{
    public sealed partial class M1CombatReview
    {
        [Serializable] public sealed class CombinedEntry { public string id, designRef, proposedContract, stage, classification; public Gear gear; public Arena arena; }
        [Serializable] public sealed class Combined { public CombinedEntry[] cases; public Arena arena; public int lordHealth, weaponSlots, toolSlots, captureStartTick; public string secondaryEnemyId; public int secondaryEnemyHealth, secondaryEnemyFirstSpawnTick; }
        [Serializable] public sealed class Source { public string id, shape, audioCue; }
        [Serializable] public sealed class SourceCount { public string id; public long activations; public int activationEvents, positiveHits; }
        struct DensitySample { public int frame, tick, global, visible; }
        readonly List<DensitySample> densities = new List<DensitySample>(16384);
        SourceCount[] sourceCounts;
        readonly long[] activationStarts = new long[8];
        readonly string[] activationKeys = new string[8];
        long lastCombinedEvent = -1;
        int enemyKilledEvents, rewardCreatedEvents, rewardCollectedEvents;
        long killExperienceAwarded, rewardExperienceCreated, rewardExperienceCollected;
        public bool IsCombined => Definition.contract == "m1-combat-combined-v1";
        static void ValidateCombined(Case d, string[] args)
        {
            if (Array.IndexOf(args,"--offline-replay-capture") >= 0 || Array.IndexOf(args,"--wave-benchmark-capture") >= 0)
                throw new ArgumentException("Combined combat stress requires capture OFF.");
            var c=d.combined;
            if(c==null || c.cases==null || c.cases.Length!=8 || c.arena==null || d.sources==null || d.sources.Length!=8 || c.lordHealth<=0 || c.captureStartTick!=d.captureStartTick || d.captureStartTick<0 || d.endTick-d.captureStartTick!=600)
                throw new InvalidDataException("Combined stress requires eight sources, finite health and a 600-tick window.");
            if (!string.IsNullOrEmpty(c.secondaryEnemyId) && (c.secondaryEnemyId==c.arena.enemyId || c.secondaryEnemyHealth<=0 || c.secondaryEnemyFirstSpawnTick<0 || c.secondaryEnemyFirstSpawnTick>=c.arena.durationTicks))
                throw new InvalidDataException("Invalid secondary stress enemy definition.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            for(var i=0;i<8;i++)
            {
                var entry=c.cases[i]; var source=d.sources[i];
                if(entry==null || entry.gear==null || entry.arena==null || entry.stage!="evolved" || entry.classification!="hypothesis" || entry.proposedContract!="m1-review-composition-v1" || string.IsNullOrEmpty(entry.id) || !ids.Add(entry.id) || source==null || source.id!=entry.id || string.IsNullOrEmpty(source.shape) || string.IsNullOrEmpty(source.audioCue))
                    throw new InvalidDataException("Combined stress sources must match eight distinct evolved cases in order.");
                entry.gear.composition=NormalizeComposition(entry.gear.composition);
            }
        }
        static WaveShowcaseArena ConvertArena(Arena a) => new WaveShowcaseArena(a.enemyId,a.spawnPeriodTicks,a.enemyCap,a.enemySpeed,a.enemyHealth,a.durationTicks,a.enemyDamage,a.suppressedUntilTick,a.baseExperience,a.spawnRadius,a.recycleRadius,a.enemyBodyWidth);
        ContentCatalog ApplyCombined(ContentCatalog baseline)
        {
            var c=Definition.combined; var cases=new WaveShowcaseCase[c.cases.Length];
            for(var i=0;i<cases.Length;i++)
            {
                var d=c.cases[i]; var g=d.gear; var p=g.composition;
                if(!Enum.TryParse<WaveAttackKind>(g.kind,true,out var kind) || !Enum.IsDefined(typeof(WaveAttackKind),kind)) throw new InvalidDataException("Unknown combined attack kind.");
                var gear=new WaveGearDefinition(g.id,g.designRef,kind,g.damage,g.range,g.cooldownTicks,g.speed,g.count,g.lifetimeTicks,g.knockback,g.workTicks,g.workRadius,g.rewardExperience,g.capacity,Aim:string.IsNullOrEmpty(g.aim)?null:g.aim,Composition:p==null?null:new WaveAttackComposition(p.version,p.placement,p.delayTicks,p.stepTicks,p.spacing,p.radius));
                cases[i]=new WaveShowcaseCase(d.id,d.designRef,d.proposedContract,d.stage,d.classification,gear,ConvertArena(d.arena));
            }
            var result=ReviewShowcaseCatalog.CreateCombined(baseline,new WaveCombinedShowcaseCase(cases,ConvertArena(c.arena),c.lordHealth,c.weaponSlots,c.toolSlots,c.captureStartTick,string.IsNullOrEmpty(c.secondaryEnemyId)?null:c.secondaryEnemyId,c.secondaryEnemyHealth,c.secondaryEnemyFirstSpawnTick));
            if(ReviewShowcaseCatalog.Hash(result)!=Definition.catalogHash) throw new InvalidDataException("Combined catalog hash mismatch.");
            return result;
        }
        public void Configure(WorldRenderer world)
        {
            world.ClearM1Combat();
            if(!IsCombined) { world.ConfigureM1Combat(Definition.id,Definition.shape,Definition.audioCue); return; }
            InitializeCounts();
            for(var i=0;i<8;i++) { var source=Definition.sources[i]; world.ConfigureM1Combat(source.id,source.shape,source.audioCue); }
        }
        void InitializeCounts()
        {
            sourceCounts=new SourceCount[8];
            for(var i=0;i<8;i++) {var id=Definition.sources[i].id; sourceCounts[i]=new SourceCount {id=id};activationKeys[i]="activation-started:"+id;}
        }
        public string CueFor(string source)
        {
            if(!IsCombined) return source==Definition.id?Definition.audioCue:null;
            for(var i=0;i<Definition.sources.Length;i++) if(Definition.sources[i].id==source) return Definition.sources[i].audioCue;
            return null;
        }
        public void ObserveCombined(WaveRuntimeFrame wave,int snapshotTick)
        {
            if(!IsCombined) return;
            ObserveCombinedCounters(wave.Counters,snapshotTick);
            for(var i=0;i<wave.Events.Count;i++) ObserveCombinedEvent(wave.Events[i]);
        }
        void ObserveCombinedCounters(IReadOnlyDictionary<string,long> counters,int snapshotTick)
        {
            if(sourceCounts==null) InitializeCounts();
            for(var i=0;i<8;i++)
            {
                counters.TryGetValue(activationKeys[i],out var value);
                if(snapshotTick<=Definition.captureStartTick) activationStarts[i]=value;
                else if(snapshotTick<=Definition.endTick) sourceCounts[i].activations=value-activationStarts[i];
            }
        }
        void ObserveCombinedEvent(WaveEvent e)
        {
            if(e.Id<=lastCombinedEvent) return;
            lastCombinedEvent=e.Id;
            if(e.Tick<Definition.captureStartTick || e.Tick>=Definition.endTick) return;
            switch(e.Kind)
            {
                case "enemy-killed": enemyKilledEvents++; killExperienceAwarded+=e.Amount; break;
                case "reward-created": rewardCreatedEvents++; rewardExperienceCreated+=e.Amount; break;
                case "reward-collected": rewardCollectedEvents++; rewardExperienceCollected+=e.Amount; break;
            }
            if(sourceCounts==null) InitializeCounts();
            for(var i=0;i<sourceCounts.Length;i++)
            {
                var s=sourceCounts[i]; if(s.id!=e.Source) continue;
                if(e.Kind=="attack" || e.Kind=="projectile-launched" || e.Kind=="chain-link" || e.Kind=="orbit-fragment" || e.Kind=="review-impact") s.activationEvents++;
                if(e.Amount>0 && (e.Kind=="hit" || e.Kind=="group-hit" || e.Kind=="trained-group-hit")) s.positiveHits++;
                break;
            }
        }
        public void RecordCombinedFrame(int frame,RunFrame snapshot,WorldRenderer world)
        {
            if(!IsCombined || snapshot.Tick<=Definition.captureStartTick || snapshot.Tick>Definition.endTick) return;
            if(densities.Count==densities.Capacity) throw new InvalidDataException("Combined density evidence capacity exceeded.");
            var visible=0;
            for(var i=0;i<snapshot.Enemies.Count;i++) if(world.M1CombatPointVisible(snapshot.Enemies[i].Position)) visible++;
            densities.Add(new DensitySample {frame=frame,tick=snapshot.Tick,global=snapshot.Enemies.Count,visible=visible});
        }
#if UNITY_EDITOR
        public void EditorObserveCombinedCounters(IReadOnlyDictionary<string,long> values,int tick) => ObserveCombinedCounters(values,tick);
        public void EditorObserveCombinedEvent(WaveEvent value) => ObserveCombinedEvent(value);
#endif
        void SaveCombined(string directory,bool verified)
        {
            var global=0; var visible=0; var both=0; var all=sourceCounts!=null;
            using(var writer=new StreamWriter(Path.Combine(directory,"m1-combat-density.csv")))
            {
                writer.WriteLine("frame,tick,globalEnemies,visibleEnemyCenters");
                foreach(var d in densities) { writer.WriteLine($"{d.frame},{d.tick},{d.global},{d.visible}"); global=Math.Max(global,d.global);visible=Math.Max(visible,d.visible);if(d.global>=800 && d.visible>=800)both++; }
            }
            if(sourceCounts!=null) foreach(var s in sourceCounts) all&=s.activations>0;
            File.WriteAllText(Path.Combine(directory,"m1-combat-review.json"),JsonUtility.ToJson(new CombinedManifest {definition=Definition,sidecarSha256=SidecarSha256,replayVerified=verified,sources=sourceCounts,maximumGlobalEnemies=global,maximumVisibleEnemyCenters=visible,framesWithGlobalAndVisible800=both,allEightSourcesActivated=all,enemyKilledEvents=enemyKilledEvents,killExperienceAwarded=killExperienceAwarded,rewardCreatedEvents=rewardCreatedEvents,rewardExperienceCreated=rewardExperienceCreated,rewardCollectedEvents=rewardCollectedEvents,rewardExperienceCollected=rewardExperienceCollected,presentation=presentation,musterPresentation=musterRecords.ToArray()},true));
        }
        [Serializable] sealed class CombinedManifest
        {
            public string contract="m1-combat-combined-native-v1",sidecarSha256;
            public string interpretation="Capture OFF, finite-health preconfigured stress with declared equipment preparation; not ordinary progression or C05. Actual Core event counters cover [start,end), rendered density covers (start,end]. Visibility counts enemy centers inside the current camera viewport, not unobscured pixels. Activations are authoritative activation-started source counter deltas; activationEvents count attack/projectile-launched/chain-link/orbit-fragment/review-impact; no synthetic shapes or damage. Frame numbers join the standard benchmark CSV; foreground exclusions remain in its manifest.";
            public int enemyKilledEvents,rewardCreatedEvents,rewardCollectedEvents;
            public long killExperienceAwarded,rewardExperienceCreated,rewardExperienceCollected;
            public PresentationSummary presentation; public MusterRecord[] musterPresentation;
            public string experienceContract="Wave enemy-killed.Amount is XP awarded immediately by Core, not a physical loot-drop or collection event. reward-created/reward-collected are actual growth reward events with their XP amounts. All counters use deduplicated [start,end) events; absent events explicitly remain zero.";
            public Case definition; public bool replayVerified,allEightSourcesActivated; public SourceCount[] sources; public int maximumGlobalEnemies,maximumVisibleEnemyCenters,framesWithGlobalAndVisible800;
        }
    }
}
