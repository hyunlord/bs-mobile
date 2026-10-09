using System;
using System.Collections.Generic;
using System.Linq;
using SowSiege.Core;
using UnityEngine;
namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        WaveRuntimeDefinition waveDefinition;
        ContentCatalog waveCatalog;
        WaveRuntimeFrame wave;
        RunFrame indexedWaveActors;
        readonly Dictionary<int,WaveEnemyView> waveEnemyById = new Dictionary<int,WaveEnemyView>();
        readonly Dictionary<int,EnemyView> waveActorById = new Dictionary<int,EnemyView>();
        readonly List<(WaveEvent value,float started)> waveEffects = new List<(WaveEvent,float)>();
        long lastWaveEvent = -1;
        public void AcceptWave(ContentCatalog catalog, WaveRuntimeFrame frame)
        {
            var profileChanged=!ReferenceEquals(waveCatalog,catalog);
            if(profileChanged||frame==null)
            {
                waveEnemyById.Clear();waveActorById.Clear();indexedWaveActors=null;
            }
            if(frame!=null&&(profileChanged||!ReferenceEquals(wave,frame)))
            {
                waveEnemyById.Clear();
                foreach(var enemy in frame.Enemies)if(!waveEnemyById.ContainsKey(enemy.Id))waveEnemyById.Add(enemy.Id,enemy);
            }
            waveCatalog=catalog;waveDefinition=catalog?.WaveRuntime;wave=frame;
            if(followCamera!=null)followCamera.KeepViewportInsideMap=frame!=null;
            if(frame==null)return;
            foreach(var value in frame.Events)
            {
                if(value.Id<=lastWaveEvent)continue;
                lastWaveEvent=value.Id;
                if(value.Kind=="attack"||value.Kind=="building-brace-swing"||value.Kind=="chain-link"||value.Kind=="harvest-fragments"||value.Kind=="evolution-activated"||value.Kind=="reward-collected")waveEffects.Add((value,visualTime));
                if(value.Kind=="hit")hitUntil[value.SubjectId]=visualTime+GameVisualTokens.HitFlashSeconds;
            }
        }
        string WaveEnemyState(int id,string source,string fallback)
        {
            if(wave==null||fallback=="death")return fallback;
            if(!waveEnemyById.TryGetValue(id,out var view))return fallback;
            var kind=waveDefinition.Enemies[source].Kind;
            if(kind==WaveEnemyKind.FloodBoss)return WaveBossState(view,fallback);
            if(fallback=="hit")return fallback;
            if(kind==WaveEnemyKind.Ranged)return view.Phase=="tell-shot"?"shoot":view.Phase=="recovery"?"retreat":fallback;
            if(kind==WaveEnemyKind.Charger)return view.Phase=="tell-charge"?"windup":view.Phase=="recovery"?"recover":fallback;
            if(kind==WaveEnemyKind.Shield)return view.Phase=="turn"?"turn":"guard";
            if(kind==WaveEnemyKind.RipeGrazer&&view.Phase.StartsWith("tell-",StringComparison.Ordinal))return "feeding";
            if(kind==WaveEnemyKind.Pursuer&&view.Phase.StartsWith("tell-",StringComparison.Ordinal))return "windup";
            return fallback;
        }
        public static string WaveBossState(WaveEnemyView view,string fallback)
            =>fallback=="death"?"death":view.Phase switch {"tell-water"=>"water-windup","tell-charge"=>"charge-windup","charge"=>"charge","water"=>"water-surge","recovery"=>view.BossPhase==0?"stuck":"recover",_=>"idle"};
        void WaveSprite(string id,int layer,WorldPoint position,float opacity=1,float scale=1,float heightScale=1)
        {
            var visual=Resolve("wave",id,"default");
            Draw(visual,layer,Point(position),visualTime,Vector2.Scale(visual.WorldSize,new Vector2(scale,scale*heightScale)),opacity:opacity);
        }
        void IndexWaveActors(RunFrame current)
        {
            if(ReferenceEquals(indexedWaveActors,current))return;
            waveActorById.Clear();
            foreach(var enemy in current.Enemies)if(!waveActorById.ContainsKey(enemy.Id))waveActorById.Add(enemy.Id,enemy);
            indexedWaveActors=current;
        }
        void DrawWave(RunFrame current)
        {
            if(wave==null)return;
            IndexWaveActors(current);
            if(wave.Timber>0)
            {
                var pile=Resolve("wave","timber-source","default");
                var fraction=Mathf.Sqrt(Mathf.Clamp01((float)wave.Timber/Mathf.Max(1,waveDefinition.InitialTimber)));
                Draw(pile,GameVisualTokens.GrowthLayer,Point(wave.TimberOrigin),visualTime,pile.WorldSize*(fraction*GameVisualTokens.WaveTimberSourceScale));
            }
            var cargo=Resolve("wave","timber-bundle","default");
            var cargoSize=cargo.WorldSize*GameVisualTokens.StockBundleScale;
            for(var i=0;i<wave.ProcessedTimber;i++)
                Draw(cargo,GameVisualTokens.ReadyLayer,Point(wave.TimberOrigin)+new Vector2((i%4+1)*cargoSize.x,(i/4)*cargoSize.y),visualTime,cargoSize);
            foreach(var detour in wave.Detours)
                Draw(Resolve("wave","bitter-dust","default"),GameVisualTokens.GrowthLayer,Point(detour.Position),visualTime,Vector2.one*detour.Radius*2/settings.WorldUnitsPerUnityUnit,opacity:GameVisualTokens.AreaAttackOpacity);
            var pathArt=Resolve("wave","sprout-path","default");
            var pathSize=pathArt.WorldSize*Mathf.Min(1,waveDefinition.PathSpacing*GameVisualTokens.WavePathSpacingFraction/settings.WorldUnitsPerUnityUnit/Mathf.Max(pathArt.WorldSize.x,pathArt.WorldSize.y));
            foreach(var path in wave.Paths)Draw(pathArt,GameVisualTokens.GrowthLayer-2,Point(path),visualTime,pathSize,opacity:GameVisualTokens.TerrainOpacity);
            foreach(var work in wave.Work)
            {
                if(work.Kind=="grain"&&work.Health<=0)continue;
                var id=work.Kind=="grain"?(work.Complete?"grain-ripe":work.Progress==0?"grain-seed":"grain-young"):
                    work.Kind=="building"?(work.Health<=0?"workshop-ruin":work.Complete?"workshop-complete":"workshop-frame"):work.Kind=="water"?(work.Wet?"pool-full":"pool-empty"):work.Kind;
                if(work.Kind=="grain")
                {
                    var crop=Resolve("wave",id,"default");
                    var maxWidth=waveCatalog.Tuning.World.Farms.Spacing*GameVisualTokens.WaveCropSpacingFraction/settings.WorldUnitsPerUnityUnit;
                    var cropSize=crop.WorldSize*Mathf.Min(1,maxWidth/crop.WorldSize.x);
                    var position=Point(work.Position);
                    position.x=Mathf.Clamp(position.x,cropSize.x*crop.Pivot.x,(float)mapWidth/settings.WorldUnitsPerUnityUnit-cropSize.x*(1-crop.Pivot.x));
                    position.y=Mathf.Clamp(position.y,cropSize.y*crop.Pivot.y,(float)mapHeight/settings.WorldUnitsPerUnityUnit-cropSize.y*(1-crop.Pivot.y));
                    Draw(crop,work.Complete?GameVisualTokens.ReadyLayer:GameVisualTokens.GrowthLayer,position,visualTime,cropSize,opacity:work.Dormant||work.Dry?GameVisualTokens.TerrainOpacity:GameVisualTokens.WaveCropOpacity);
                }
                else WaveSprite(id,work.Complete?GameVisualTokens.ReadyLayer:GameVisualTokens.GrowthLayer,work.Position,
                    work.Dormant||work.Dry?GameVisualTokens.TerrainOpacity:work.Kind=="water"?GameVisualTokens.WavePoolOpacity:1,
                    work.Kind=="water"?GameVisualTokens.WavePoolScale:GameVisualTokens.WaveWorkshopScale);
                if(work.ParentId>=0)
                { var parent=wave.Work.FirstOrDefault(w=>w.Id==work.ParentId);
                  WaveSprite(parent!=null&&parent.Health>0&&parent.Complete&&work.Protected?"roof-intact":"roof-broken",GameVisualTokens.ReadyLayer,work.Position,scale:GameVisualTokens.WaveWorkshopScale); }
                if(work.ShipmentActive)WaveSprite("timber-bundle",GameVisualTokens.ReadyLayer+1,work.Position,scale:GameVisualTokens.StockBundleScale);
            }
            if(wave.CarriedWater>0)WaveSprite("water-carry",GameVisualTokens.AllyLayer+1,current.Lord.Position,GameVisualTokens.WaveWetOpacity,GameVisualTokens.WaveStatusScale);
            foreach(var reward in wave.Rewards)
            {
                var source=reward.Source;
                if(waveDefinition.Evolutions.TryGetValue(source,out var evolution))source=evolution.InputIds.First(id=>waveDefinition.Gear[id].Kind>=WaveAttackKind.SeedFan);
                var kind=waveDefinition.Gear[source].Kind;
                var id=kind==WaveAttackKind.WaterFan?"xp-water":kind==WaveAttackKind.ConstructionSlam?"xp-timber":kind==WaveAttackKind.MusterWave?"xp-mission":"xp-grain";
                WaveSprite(id,GameVisualTokens.ExperienceLayer,reward.Position,scale:GameVisualTokens.WaveRewardScale);
            }
            foreach(var group in wave.Groups)
            {
                var activity=group.Phase=="returning"?"return":group.Phase=="idle"?"idle":"muster";
                var representative=Resolve("person",group.Formation=="cover"?"guard":"militia",activity);
                var position=Point(group.Position);
                Draw(representative,GameVisualTokens.AllyLayer,position,visualTime+group.Id*.13f);
                if(group.Training>0)
                {
                    var second=Resolve("person",group.Formation=="cover"?"militia":"guard",activity);
                    var spacing=representative.WorldSize*GameVisualTokens.GroupRepresentativeOffset;
                    Draw(second,GameVisualTokens.AllyLayer+(group.FrontRank==0?-1:1),position+new Vector2(spacing.x,group.FrontRank==0?-spacing.y:spacing.y),visualTime+group.Id*.13f);
                }
                if(group.ReservedFood>0)WaveSprite("ration",GameVisualTokens.AllyLayer+1,group.Position);
            }
            foreach(var attack in wave.Attacks)
                if(attack.ExpireTick>current.Tick)
                {
                    var fragment=Resolve("wave","shield-fragment","default");
                    var diameter=2f*attack.Radius/settings.WorldUnitsPerUnityUnit;
                    var scale=Mathf.Min(GameVisualTokens.WaveFragmentScale,diameter/Mathf.Max(fragment.WorldSize.x,fragment.WorldSize.y));
                    Draw(fragment,GameVisualTokens.AttackLayer,Point(attack.Position),visualTime,fragment.WorldSize*scale);
                }
            foreach(var projectile in wave.Projectiles)
            {
                var visual=Resolve("attack",projectile.Source,"projectile");
                Draw(visual,projectile.Hostile?GameVisualTokens.EnemyLayer:GameVisualTokens.AttackLayer,Point(projectile.Position),visualTime,degrees:Angle(Point(projectile.Position)-Point(projectile.Previous)),opacity:GameVisualTokens.TravellingAttackOpacity);
            }
            foreach(var enemy in wave.Enemies)
            {
                if(!waveActorById.TryGetValue(enemy.Id,out var actor))continue;
                if(enemy.Wet)WaveSprite("wet",GameVisualTokens.GrowthLayer,actor.Position,GameVisualTokens.WaveWetOpacity,GameVisualTokens.WaveStatusScale,GameVisualTokens.WaveWetHeight);
                if(enemy.Stopped)WaveSprite("stopped",GameVisualTokens.GrowthLayer+1,actor.Position,GameVisualTokens.WaveWetOpacity,GameVisualTokens.WaveStatusScale);
                if(enemy.Phase=="tell-charge"||enemy.Phase=="tell-water"||enemy.Phase=="water")
                {
                    var a=Point(enemy.Origin);var b=Point(enemy.Target);var visual=Resolve("wave",enemy.Phase.Contains("water")?"water-lane":"charge-tell","default");
                    var diameter=waveCatalog.Enemies[actor.DefinitionId].Range*2f/settings.WorldUnitsPerUnityUnit;
                    var opacity=enemy.Phase=="water"?GameVisualTokens.WaveHostileActiveOpacity:GameVisualTokens.WaveHostileTellOpacity;
                    Draw(visual,GameVisualTokens.AttackLayer,(a+b)*.5f,visualTime,new Vector2((b-a).magnitude,diameter),Angle(b-a),opacity);
                    var cap=Resolve("attack","core:levy_banner","nova");
                    var tint=enemy.Phase.Contains("water")?Color.white:GameVisualTokens.Hostile;
                    Draw(cap,GameVisualTokens.AttackLayer,a,0,Vector2.one*diameter,opacity:opacity,tint:tint);
                    Draw(cap,GameVisualTokens.AttackLayer,b,0,Vector2.one*diameter,opacity:opacity,tint:tint);
                }
            }
            for(var i=waveEffects.Count-1;i>=0;i--)
            {
                var effect=waveEffects[i];var age=visualTime-effect.started;var duration=effect.value.Kind=="evolution-activated"?GameVisualTokens.EmphasisSeconds:WorldEffects.AttackLifetimeSeconds;
                if(age>=duration){waveEffects.RemoveAt(i);continue;}
                var value=effect.value;var a=Point(value.Position);var b=Point(value.Target);var opacity=1-age/duration;
                if(value.Kind=="evolution-activated"){Feedback("evolution",GameVisualTokens.LordLayer-2,a,age/duration);continue;}
                if(value.Kind=="reward-collected"){Feedback("harvest",GameVisualTokens.ExperienceLayer,a,age/duration);continue;}
                if(value.Kind=="harvest-fragments"){WaveSprite("grain-fragment",GameVisualTokens.AttackLayer,value.Position,opacity);continue;}
                if(value.Kind=="building-brace-swing"){Draw(Resolve("wave","wood-brace","default"),GameVisualTokens.AttackLayer,a,age,Vector2.one*value.Amount*2/settings.WorldUnitsPerUnityUnit,Angle(b-a),opacity*GameVisualTokens.AreaAttackOpacity);continue;}
                if(value.Kind=="chain-link") {var art=Resolve("attack",value.Source,"chain");Draw(art,GameVisualTokens.AttackLayer,(a+b)*.5f,age,new Vector2((b-a).magnitude,GameVisualTokens.AttackRibbonWidth),Angle(b-a),opacity);continue;}
                var attackSource=value.Source;
                if(waveDefinition.Evolutions.TryGetValue(attackSource,out var evolved))attackSource=evolved.InputIds.First(id=>waveDefinition.Gear[id].Kind==(evolved.Kind==WaveEvolutionKind.ShelteredPlot?WaveAttackKind.ConstructionSlam:WaveAttackKind.Arc));
                if(waveDefinition.Gear.TryGetValue(attackSource,out var gear))
                {
                    var form=gear.Kind==WaveAttackKind.Arc?"sector90":gear.Kind==WaveAttackKind.HarvestArc?"sector90":gear.Kind==WaveAttackKind.ConstructionSlam?"melee":gear.Kind==WaveAttackKind.SeedFan?"projectile":"wave";
                    var visual=Resolve("attack",value.Source,form);
                    var radius=(float)value.Amount/settings.WorldUnitsPerUnityUnit;
                    var size=gear.Kind==WaveAttackKind.Arc||gear.Kind==WaveAttackKind.HarvestArc?visual.WorldSize*radius:Vector2.one*radius*2;
                    Draw(visual,GameVisualTokens.AttackLayer,a,age,size,Angle(b-a),opacity*GameVisualTokens.AreaAttackOpacity);
                }
            }
        }
    }
}
