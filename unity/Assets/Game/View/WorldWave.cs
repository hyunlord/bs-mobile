using System;
using System.Collections.Generic;
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
        readonly Dictionary<int,WaveWorkView> waveWorkById = new Dictionary<int,WaveWorkView>();
        readonly Dictionary<WorldPoint,WaveWorkView> unfinishedBuildingByPosition = new Dictionary<WorldPoint,WaveWorkView>();
        readonly List<string> expiredRepairAnchors = new List<string>();
        readonly List<(WaveEvent value,float started)> waveEffects = new List<(WaveEvent,float)>();
        readonly HashSet<int> liveGrainIds = new HashSet<int>();
        readonly Dictionary<string,(int buildingId,float started)> repairAnchors = new Dictionary<string,(int,float)>();
        readonly HashSet<string> drawnRepairAnchors = new HashSet<string>();
        long lastWaveEvent = -1;
        int ripeCueId = -1;
        string collectionLabel;
        float collectedAt;
        readonly WaveRenderInterpolation waveMotion = new WaveRenderInterpolation();
        readonly Dictionary<string,string> rewardSources = new Dictionary<string,string>(StringComparer.Ordinal);
        readonly Dictionary<string,string> attackSources = new Dictionary<string,string>(StringComparer.Ordinal);
        float heroAttackUntil;
        public void AcceptWave(ContentCatalog catalog, WaveRuntimeFrame frame)
        {
            var profileChanged=!ReferenceEquals(waveCatalog,catalog);
            if(profileChanged||frame==null)
            {
                waveEnemyById.Clear();waveActorById.Clear();waveWorkById.Clear();unfinishedBuildingByPosition.Clear();expiredRepairAnchors.Clear();indexedWaveActors=null;
                repairAnchors.Clear();drawnRepairAnchors.Clear();liveGrainIds.Clear();waveEffects.Clear();lastWaveEvent=-1;
                ResetFallow();
                ResetThreatWarnings();
                waveMotion.Reset();
                heroAttackUntil=0;
                rewardSources.Clear(); attackSources.Clear();
                if(catalog?.WaveRuntime!=null)
                    foreach(var pair in catalog.WaveRuntime.Evolutions)
                    {
                        var evolution=pair.Value;
                        for(var input=0;input<evolution.InputIds.Length;input++)
                        {
                            var id=evolution.InputIds[input];var kind=catalog.WaveRuntime.Gear[id].Kind;
                            if(kind>=WaveAttackKind.SeedFan && !rewardSources.ContainsKey(pair.Key))rewardSources.Add(pair.Key,id);
                            if(kind==(evolution.Kind==WaveEvolutionKind.ShelteredPlot?WaveAttackKind.ConstructionSlam:WaveAttackKind.Arc) && !attackSources.ContainsKey(pair.Key))attackSources.Add(pair.Key,id);
                        }
                    }
                ripeCueId=-1;
                collectionLabel=null;growthLabels?.Clear();
            }
            if(frame!=null&&(profileChanged||!ReferenceEquals(wave,frame)))
            {
                waveEnemyById.Clear();
                for(var i=0;i<frame.Enemies.Count;i++){var enemy=frame.Enemies[i];if(!waveEnemyById.ContainsKey(enemy.Id))waveEnemyById.Add(enemy.Id,enemy);}
                liveGrainIds.Clear();waveWorkById.Clear();unfinishedBuildingByPosition.Clear();
                for(var workIndex=0;workIndex<frame.Work.Count;workIndex++)
                {
                    var work=frame.Work[workIndex];
                    if(!waveWorkById.ContainsKey(work.Id))waveWorkById.Add(work.Id,work);
                    if(work.Kind=="grain"&&work.Health>0)liveGrainIds.Add(work.Id);
                    if(work.Kind=="building"&&work.Health>0&&!work.Complete&&!unfinishedBuildingByPosition.ContainsKey(work.Position))unfinishedBuildingByPosition.Add(work.Position,work);
                }
            }
            waveCatalog=catalog;waveDefinition=catalog?.WaveRuntime;wave=frame;
            if(followCamera!=null)followCamera.KeepViewportInsideMap=frame!=null;
            if(frame==null)return;
            waveMotion.Accept(frame,visualTime);
            for(var valueIndex=0;valueIndex<frame.Events.Count;valueIndex++)
            {
                var value=frame.Events[valueIndex];
                if(value.Id<=lastWaveEvent)continue;
                lastWaveEvent=value.Id;
                AcceptFallowEvent(value);
                if(value.Kind=="reward-collected"&&value.Amount>0)
                {
                    var source=value.Source;
                    if(rewardSources.TryGetValue(source,out var rewardSource))source=rewardSource;
                    collectionLabel=(waveDefinition.Gear[source].Kind==WaveAttackKind.SeedFan?"수확 +":"완료 +")+value.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)+" 경험치";
                    collectedAt=visualTime;
                }
                if(value.Kind=="attack"||value.Kind=="building-brace-swing"||value.Kind=="chain-link"||value.Kind=="harvest-fragments"||value.Kind=="evolution-activated"||value.Kind=="harvest-complete"||(value.Kind=="reward-collected"&&value.Amount>0))waveEffects.Add((value,visualTime));
                if(value.Kind=="work-created"&&liveGrainIds.Contains(value.SubjectId))waveEffects.Add((value,visualTime));
                if(value.Kind=="hit")hitUntil[value.SubjectId]=visualTime+GameVisualTokens.HitFlashSeconds;
                if(value.Kind=="attack")heroAttackUntil=visualTime+GameVisualTokens.ActorAttackSeconds;
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
            =>WaveSpriteAt(id,layer,Point(position),opacity,scale,heightScale);
        void WaveSpriteAt(string id,int layer,Vector2 position,float opacity=1,float scale=1,float heightScale=1)
        {
            var visual=Resolve("wave",id,"default");
            Draw(visual,layer,position,visualTime,Vector2.Scale(visual.WorldSize,new Vector2(scale,scale*heightScale)),opacity:opacity);
        }
        void IndexWaveActors(RunFrame current)
        {
            if(ReferenceEquals(indexedWaveActors,current))return;
            waveActorById.Clear();
            for(var i=0;i<current.Enemies.Count;i++){var enemy=current.Enemies[i];if(!waveActorById.ContainsKey(enemy.Id))waveActorById.Add(enemy.Id,enemy);}
            indexedWaveActors=current;
        }
        void DrawWave(RunFrame current,float alpha,Vector2 lordPoint)
        {
            growthLabels?.BeginFrame();
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
            for(var i=0;i<wave.Detours.Count;i++)
            {
                var detour=wave.Detours[i];
                Draw(Resolve("wave","bitter-dust","default"),GameVisualTokens.GrowthLayer,Point(detour.Position),visualTime,Vector2.one*detour.Radius*2/settings.WorldUnitsPerUnityUnit,opacity:GameVisualTokens.AreaAttackOpacity);
            }
            var pathArt=Resolve("wave","sprout-path","default");
            var pathSize=pathArt.WorldSize*Mathf.Min(1,waveDefinition.PathSpacing*GameVisualTokens.WavePathSpacingFraction/settings.WorldUnitsPerUnityUnit/Mathf.Max(pathArt.WorldSize.x,pathArt.WorldSize.y));
            for(var i=0;i<wave.Paths.Count;i++)Draw(pathArt,GameVisualTokens.GrowthLayer-2,Point(wave.Paths[i]),visualTime,pathSize,opacity:GameVisualTokens.TerrainOpacity);
            WaveWorkView nearbyRipe=null,retainedRipe=null;
            var nearestRipeDistance=GameVisualTokens.WaveRipeCueRadius*GameVisualTokens.WaveRipeCueRadius;
            if(collectionLabel!=null)
            {
                var progress=(visualTime-collectedAt)/GameVisualTokens.WaveCollectionLabelSeconds;
                if(progress<1)growthLabels.Show(1,collectionLabel,lordPoint+Vector2.up*(GameVisualTokens.WaveCollectionLabelRise+progress*.15f),Mathf.Min(1,(1-progress)*3));
                else collectionLabel=null;
            }
            for(var workIndex=0;workIndex<wave.Work.Count;workIndex++)
            {
                var work=wave.Work[workIndex];
                if(work.Kind=="grain"&&work.Health<=0)continue;
                var id=work.Kind=="grain"?(work.Complete?"grain-ripe":work.Progress==0?"grain-seed":"grain-young"):
                    work.Kind=="building"?(work.Health<=0?"workshop-ruin":work.Complete?"workshop-complete":"workshop-frame"):work.Kind=="water"?(work.Wet?"pool-full":"pool-empty"):work.Kind;
                if(work.Kind=="grain")
                {
                    var crop=Resolve("wave",id,"default");
                    var maxWidth=waveCatalog.Tuning.World.Farms.Spacing*GameVisualTokens.WaveCropSpacingFraction/settings.WorldUnitsPerUnityUnit;
                    if(FallowActive) maxWidth=Mathf.Max(maxWidth,work.Complete?GameVisualTokens.FallowRipeWidth:work.Progress==0?GameVisualTokens.FallowSeedWidth:GameVisualTokens.FallowSproutWidth);
                    var cropScale=maxWidth/crop.WorldSize.x;
                    var cropSize=crop.WorldSize*(FallowActive?cropScale:Mathf.Min(1,cropScale));
                    var position=Point(work.Position);
                    position.x=Mathf.Clamp(position.x,cropSize.x*crop.Pivot.x,(float)mapWidth/settings.WorldUnitsPerUnityUnit-cropSize.x*(1-crop.Pivot.x));
                    position.y=Mathf.Clamp(position.y,cropSize.y*crop.Pivot.y,(float)mapHeight/settings.WorldUnitsPerUnityUnit-cropSize.y*(1-crop.Pivot.y));
                    Draw(crop,work.Complete?GameVisualTokens.ReadyLayer:GameVisualTokens.GrowthLayer,position,visualTime,cropSize,opacity:work.Dormant||work.Dry?GameVisualTokens.TerrainOpacity:FallowActive?1:GameVisualTokens.WaveCropOpacity);
                    if(work.Complete)
                    {
                        Draw(crop,GameVisualTokens.ReadyLayer+1,position,visualTime,cropSize,opacity:GameVisualTokens.WaveRipeEdgeOpacity,tint:GameVisualTokens.Ready,edgeTexels:GameVisualTokens.WaveEdgeTexels);
                        var distance=(Point(work.Position)-lordPoint).sqrMagnitude;
                        if(distance<=GameVisualTokens.WaveRipeCueRadius*GameVisualTokens.WaveRipeCueRadius&&work.Id==ripeCueId)retainedRipe=work;
                        if(distance<nearestRipeDistance||(distance==nearestRipeDistance&&(nearbyRipe==null||work.Id<nearbyRipe.Id)))
                        {nearbyRipe=work;nearestRipeDistance=distance;}
                    }
                }
                else WaveSprite(id,work.Complete?GameVisualTokens.ReadyLayer:GameVisualTokens.GrowthLayer,work.Position,
                    work.Dormant||work.Dry?GameVisualTokens.TerrainOpacity:work.Kind=="water"?GameVisualTokens.WavePoolOpacity:1,
                    work.Kind=="water"?GameVisualTokens.WavePoolScale:FallowActive?GameVisualTokens.FallowWorkshopScale:GameVisualTokens.WaveWorkshopScale);
                if(FallowActive && work.Kind=="building" && work.Health>0 && !work.Complete)
                {
                    var fraction=Mathf.Clamp01((float)work.Progress/Mathf.Max(1,work.Required));
                    WaveSprite("workshop-complete",GameVisualTokens.GrowthLayer+1,work.Position,
                        Mathf.SmoothStep(0,.75f,fraction),GameVisualTokens.FallowWorkshopScale);
                }
                if(work.ParentId>=0)
                {
                    waveWorkById.TryGetValue(work.ParentId,out var parent);
                    var roof=Resolve("wave",parent!=null&&parent.Health>0&&parent.Complete&&work.Protected?"roof-intact":"roof-broken","default");
                    var roofSize=roof.WorldSize*(FallowActive?GameVisualTokens.FallowWorkshopScale:GameVisualTokens.WaveWorkshopScale);
                    Draw(roof,GameVisualTokens.ReadyLayer,Point(work.Position),visualTime,roofSize);
                    DrawWaveAttackEdge(roof,Point(work.Position),visualTime,roofSize,0,GameVisualTokens.Ally);
                }
                if(work.ShipmentActive)WaveSprite("timber-bundle",GameVisualTokens.ReadyLayer+1,work.Position,scale:GameVisualTokens.StockBundleScale);
            }
            var ripeCue=retainedRipe??nearbyRipe;
            ripeCueId=ripeCue?.Id??-1;
            if(ripeCue!=null)
            {
                var ear=Resolve("wave","grain-ripe","default");
                Draw(ear,GameVisualTokens.WaveReadinessCueLayer,Point(ripeCue.Position)+Vector2.up*GameVisualTokens.WaveRipeCueRise,0,ear.WorldSize*GameVisualTokens.WaveRipeCueScale);
                growthLabels.Show(0,"익음",Point(ripeCue.Position)+Vector2.up*GameVisualTokens.WaveGrowthLabelRise);
            }
            if(wave.CarriedWater>0)WaveSpriteAt("water-carry",GameVisualTokens.AllyLayer+1,lordPoint,GameVisualTokens.WaveWetOpacity,GameVisualTokens.WaveStatusScale);
            for(var rewardIndex=0;rewardIndex<wave.Rewards.Count;rewardIndex++)
            {
                var reward=wave.Rewards[rewardIndex];
                var source=reward.Source;
                if(rewardSources.TryGetValue(source,out var rewardSource))source=rewardSource;
                var kind=waveDefinition.Gear[source].Kind;
                var id=kind==WaveAttackKind.WaterFan?"xp-water":kind==WaveAttackKind.ConstructionSlam?"xp-timber":kind==WaveAttackKind.MusterWave?"xp-mission":"xp-grain";
                WaveSprite(id,GameVisualTokens.GrowthLayer+1,reward.Position,GameVisualTokens.WaveRewardOpacity,GameVisualTokens.WaveRewardScale);
            }
            for(var groupIndex=0;groupIndex<wave.Groups.Count;groupIndex++)
            {
                var group=wave.Groups[groupIndex];
                var activity=group.Phase=="returning"?"return":group.Phase=="idle"?"idle":"muster";
                var representative=Resolve("person",group.Formation=="cover"?"guard":"militia",activity);
                var position=waveMotion.GroupPosition(group,alpha,settings.WorldUnitsPerUnityUnit);
                RenderedEntitySample?.Invoke("group",group.Id,position);
                var groupFacing=Facing("group",group.Id,waveMotion.GroupDirection(group));
                DrawActor(representative,GameVisualTokens.AllyLayer,position,visualTime+group.Id*.13f,representative.WorldSize,group.Phase!="idle",-1,-1,facing:groupFacing);
                if(group.Training>0)
                {
                    var second=Resolve("person",group.Formation=="cover"?"militia":"guard",activity);
                    var spacing=representative.WorldSize*GameVisualTokens.GroupRepresentativeOffset;
                    DrawActor(second,GameVisualTokens.AllyLayer+(group.FrontRank==0?-1:1),position+new Vector2(spacing.x,group.FrontRank==0?-spacing.y:spacing.y),visualTime+group.Id*.13f,second.WorldSize,group.Phase!="idle",-1,-1,facing:groupFacing);
                }
                if(group.ReservedFood>0)WaveSpriteAt("ration",GameVisualTokens.AllyLayer+1,position);
            }
            drawnRepairAnchors.Clear();
            for(var attackIndex=0;attackIndex<wave.Attacks.Count;attackIndex++)
            {
                var attack=wave.Attacks[attackIndex];
                if(attack.ExpireTick>current.Tick)
                {
                    if(waveDefinition.Evolutions.TryGetValue(attack.Source,out var orbit)&&orbit.Kind==WaveEvolutionKind.RepairOrbit&&drawnRepairAnchors.Add(attack.Source))
                    {
                        WaveWorkView anchor=null;
                        if(!attack.Origin.Equals(current.Lord.Position))unfinishedBuildingByPosition.TryGetValue(attack.Origin,out anchor);
                        if(anchor!=null)
                        {
                            if(!repairAnchors.TryGetValue(attack.Source,out var before)||before.buildingId!=anchor.Id)repairAnchors[attack.Source]=(anchor.Id,visualTime);
                            var pulse=Mathf.Clamp01(1-(visualTime-repairAnchors[attack.Source].started)/GameVisualTokens.EmphasisSeconds);
                            var badge=Resolve("attack","core:levy_banner","nova");
                            Draw(badge,GameVisualTokens.ReadyLayer+1,Point(attack.Origin),0,badge.WorldSize*GameVisualTokens.WaveWorkshopScale,opacity:GameVisualTokens.WaveRepairBannerOpacity+pulse*GameVisualTokens.AreaAttackOpacity);
                            var workshop=Resolve("wave","workshop-frame","default");
                            Draw(workshop,GameVisualTokens.WaveReadinessCueLayer,Point(anchor.Position),visualTime,workshop.WorldSize*GameVisualTokens.WaveWorkshopScale,opacity:GameVisualTokens.WaveEdgeOpacity,tint:GameVisualTokens.Attack,edgeTexels:GameVisualTokens.WaveEdgeTexels);
                            var shield=Resolve("wave","shield-fragment","default");
                            Draw(shield,GameVisualTokens.WaveTransientCueLayer,Point(attack.Origin)+Vector2.up*GameVisualTokens.WaveRipeCueRise,0,shield.WorldSize*GameVisualTokens.WaveRepairShieldScale);
                            growthLabels.Show(2,"수리 중",Point(attack.Origin)+Vector2.up*GameVisualTokens.WaveGrowthLabelRise);
                        }
                        else repairAnchors.Remove(attack.Source);
                    }
                    var fragment=Resolve("wave","shield-fragment","default");
                    var diameter=2f*attack.Radius/settings.WorldUnitsPerUnityUnit;
                    var scale=Mathf.Min(GameVisualTokens.WaveFragmentScale,diameter/Mathf.Max(fragment.WorldSize.x,fragment.WorldSize.y));
                    var position=waveMotion.OrbitPosition(attackIndex,alpha,visualTime,settings.WorldUnitsPerUnityUnit,current.Lord.Position,lordPoint);
                    RenderedEntitySample?.Invoke("orbit",waveMotion.OrbitSampleId(attackIndex),position);
                    Draw(fragment,GameVisualTokens.AttackLayer,position,visualTime,fragment.WorldSize*scale);
                    DrawWaveAttackEdge(fragment,position,visualTime,fragment.WorldSize*scale,0,GameVisualTokens.Attack);
                }
            }
            expiredRepairAnchors.Clear();
            foreach(var source in repairAnchors.Keys)if(!drawnRepairAnchors.Contains(source))expiredRepairAnchors.Add(source);
            foreach(var source in expiredRepairAnchors)repairAnchors.Remove(source);
            for(var projectileIndex=0;projectileIndex<wave.Projectiles.Count;projectileIndex++)
            {
                var projectile=wave.Projectiles[projectileIndex];
                var visual=Resolve("attack",projectile.Source,"projectile");
                var direction=waveMotion.ProjectileAngle(projectile,alpha);
                var position=Vector2.Lerp(Point(projectile.Previous),Point(projectile.Position),alpha);
                RenderedEntitySample?.Invoke("projectile",projectile.Id,position);
                Draw(visual,projectile.Hostile?GameVisualTokens.WaveDangerLayer:GameVisualTokens.AttackLayer,position,visualTime,degrees:direction,opacity:GameVisualTokens.TravellingAttackOpacity);
                if(!projectile.Hostile)DrawWaveAttackEdge(visual,position,visualTime,visual.WorldSize,direction,GameVisualTokens.Attack);
            }
            for(var enemyIndex=0;enemyIndex<wave.Enemies.Count;enemyIndex++)
            {
                var enemy=wave.Enemies[enemyIndex];
                if(!waveActorById.TryGetValue(enemy.Id,out var actor))continue;
                var actorPosition=Interpolate(previousEnemies,enemy.Id,actor.Position,alpha);
                if(enemy.Wet)WaveSpriteAt("wet",GameVisualTokens.GrowthLayer,actorPosition,GameVisualTokens.WaveWetOpacity,GameVisualTokens.WaveStatusScale,GameVisualTokens.WaveWetHeight);
                if(enemy.Stopped)WaveSpriteAt("stopped",GameVisualTokens.GrowthLayer+1,actorPosition,GameVisualTokens.WaveWetOpacity,GameVisualTokens.WaveStatusScale);
            }
            DrawThreatWarnings(current,alpha,lordPoint);
            var intakeDrawn=false;var harvestCues=0;
            for(var i=waveEffects.Count-1;i>=0;i--)
            {
                var effect=waveEffects[i];var age=visualTime-effect.started;var duration=effect.value.Kind=="evolution-activated"?GameVisualTokens.EmphasisSeconds:effect.value.Kind=="work-created"||effect.value.Kind=="harvest-complete"?GameVisualTokens.HarvestSeconds:effect.value.Kind=="reward-collected"?GameVisualTokens.ExperienceSeconds:WorldEffects.AttackLifetimeSeconds;
                if(age>=duration){waveEffects.RemoveAt(i);continue;}
                var value=effect.value;var a=Point(value.Position);var b=Point(value.Target);var opacity=1-age/duration;
                if(value.Kind=="evolution-activated"){Feedback("evolution",GameVisualTokens.LordLayer-2,a,age/duration);continue;}
                if(value.Kind=="work-created")
                {
                    if(liveGrainIds.Contains(value.SubjectId))
                        WaveSprite("grain-seed",GameVisualTokens.AttackLayer-1,value.Position,opacity,GameVisualTokens.WaveStatusScale*(.5f+.5f*age/duration));
                    continue;
                }
                if(value.Kind=="reward-collected")
                {
                    if(intakeDrawn)continue;
                    intakeDrawn=true;
                    var uptake=Resolve("feedback","harvest-experience","default");
                    var progress=age/duration;
                    var position=Vector2.Lerp(a,lordPoint,progress)+Vector2.up*GameVisualTokens.WaveIntakeRise*(.5f+.5f*progress);
                    Draw(uptake,GameVisualTokens.WaveTransientCueLayer,position,0,uptake.WorldSize*GameVisualTokens.WaveIntakeScale,opacity:opacity);
                    continue;
                }
                if(value.Kind=="harvest-complete")
                {
                    if(harvestCues++>=GameVisualTokens.WaveHarvestCueLimit)continue;
                    var ear=Resolve("wave","grain-ripe","default");
                    Draw(ear,GameVisualTokens.WaveTransientCueLayer,a+Vector2.up*GameVisualTokens.WaveRipeCueRise*(1+age/duration),0,ear.WorldSize*GameVisualTokens.WaveRipeCueScale,opacity:opacity);
                    continue;
                }
                if(value.Kind=="harvest-fragments")
                {
                    var fragments=Resolve("wave","grain-fragment","default");
                    Draw(fragments,GameVisualTokens.AttackLayer,a,age,opacity:opacity);
                    DrawWaveAttackEdge(fragments,a,age,fragments.WorldSize,0,GameVisualTokens.Ready,opacity);
                    continue;
                }
                if(value.Kind=="building-brace-swing")
                {
                    var brace=Resolve("wave","wood-brace","default");var size=Vector2.one*value.Amount*2/settings.WorldUnitsPerUnityUnit;
                    Draw(brace,GameVisualTokens.AttackLayer,a,age,size,Angle(b-a),opacity*GameVisualTokens.AreaAttackOpacity);
                    DrawWaveAttackEdge(brace,a,age,size,Angle(b-a),GameVisualTokens.Attack,opacity);
                    continue;
                }
                if(value.Kind=="chain-link")
                {
                    var revealed=Vector2.Lerp(a,b,Mathf.Clamp01(age/(duration*.18f)));
                    var art=Resolve("attack",value.Source,"chain");var size=new Vector2((revealed-a).magnitude,GameVisualTokens.WaveChainRibbonWidth);
                    var outline=renderCamera.orthographicSize*2/Mathf.Max(1,renderCamera.pixelHeight)*GameVisualTokens.WaveChainOutlinePixels*2;
                    Draw(art,GameVisualTokens.WaveAttackEdgeLayer,(a+revealed)*.5f,age,new Vector2(size.x,size.y+outline),Angle(b-a),opacity,tint:GameVisualTokens.Ink);
                    Draw(art,GameVisualTokens.WaveTransientCueLayer,(a+revealed)*.5f,age,size,Angle(b-a),opacity);
                    continue;
                }
                var attackSource=value.Source;
                waveDefinition.Evolutions.TryGetValue(attackSource,out var evolved);
                if(attackSources.TryGetValue(attackSource,out var baseSource))attackSource=baseSource;
                if(waveDefinition.Gear.TryGetValue(attackSource,out var gear))
                {
                    var form=gear.Kind==WaveAttackKind.Arc?"sector90":gear.Kind==WaveAttackKind.HarvestArc?"sector90":gear.Kind==WaveAttackKind.ConstructionSlam?"melee":gear.Kind==WaveAttackKind.SeedFan?"projectile":"wave";
                    var visual=Resolve("attack",value.Source,form);
                    var radius=(float)value.Amount/settings.WorldUnitsPerUnityUnit;
                    var size=gear.Kind==WaveAttackKind.Arc||gear.Kind==WaveAttackKind.HarvestArc?visual.WorldSize*radius:Vector2.one*radius*2;
                    var planting=evolved!=null&&evolved.Kind==WaveEvolutionKind.PlantingArc;
                    Draw(visual,GameVisualTokens.AttackLayer,a,age,size,Angle(b-a),opacity*GameVisualTokens.WaveAttackBodyOpacity,tint:planting?GameVisualTokens.Ready:Color.white);
                    DrawWaveAttackEdge(visual,a,age,size,Angle(b-a),planting?GameVisualTokens.Ready:GameVisualTokens.Attack,opacity);
                    if(planting)
                    {
                        Draw(visual,GameVisualTokens.AttackLayer,a,age,size*GameVisualTokens.WavePlantingInnerScale,Angle(b-a),opacity*GameVisualTokens.WaveAttackBodyOpacity,tint:GameVisualTokens.Ready);
                        DrawWaveAttackEdge(visual,a,age,size*GameVisualTokens.WavePlantingInnerScale,Angle(b-a),GameVisualTokens.Ready,opacity);
                    }
                }
            }
        }
        void DrawWaveAttackEdge(ArtVisual visual,Vector2 position,float time,Vector2 size,float direction,Color color,float opacity=1)
            =>Draw(visual,GameVisualTokens.WaveAttackEdgeLayer,position,time,size,direction,opacity*GameVisualTokens.WaveEdgeOpacity,tint:Color.Lerp(color,GameVisualTokens.Ink,.8f),edgeTexels:GameVisualTokens.WaveEdgeTexels);
    }
}
