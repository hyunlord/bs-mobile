using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using System.Linq;
#endif
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer : MonoBehaviour
    {
        readonly Dictionary<(string kind, string id, string state), ArtVisual> visuals = new Dictionary<(string, string, string), ArtVisual>();
        readonly Dictionary<(int layer, Texture2D texture), SpriteBatch> batches = new Dictionary<(int, Texture2D), SpriteBatch>();
        readonly Dictionary<int, Vector2> previousEnemies = new Dictionary<int, Vector2>();
        readonly Dictionary<int, Vector2> previousPeople = new Dictionary<int, Vector2>();
        readonly Dictionary<int, BuildingProgressView> construction = new Dictionary<int, BuildingProgressView>();
        readonly Dictionary<int, string> activities = new Dictionary<int, string>();
        readonly Dictionary<int, float> hitUntil = new Dictionary<int, float>();
        readonly List<int> expiredHits = new List<int>();
        readonly HashSet<string> activeAttackSources = new HashSet<string>(StringComparer.Ordinal);
        readonly WorldEffects effects = new WorldEffects();
        readonly ThreatMarkers threats = new ThreatMarkers();
        RunFrame indexedPrevious, acceptedFrame;
        FirstPlayableFrame firstPlayable;
        Camera renderCamera;
        RunCamera followCamera;
        WorldCameraSettings settings;
        WorldDamageNumbers numbers;
        WorldGrowthLabels growthLabels;
        WorldAnnouncements announcements;
        ArtCatalog art;
        ShapeMeshes lordMarkerMeshes;
        ShapeBatch lordMarker;
        static readonly Vector2[] HeroOutlineDirections = {Vector2.left,Vector2.right,Vector2.up,Vector2.down};
        Shader shader;
        string estateId;
        MetaTerrain[] chapterTerrain=Array.Empty<MetaTerrain>();
#if UNITY_EDITOR
        public string[] CaptureLayerUvsForTesting(int layer)
            =>batches.Where(pair=>pair.Key.layer==layer).OrderBy(pair=>pair.Key.texture.name,StringComparer.Ordinal)
                .SelectMany(pair=>pair.Value.CaptureSingleDrawUvsForTesting().Select(uv=>pair.Key.texture.name+":"+uv.ToString("R"))).ToArray();
#endif
        public void SetChapterTerrain(MetaTerrain[] terrain)
        {
            chapterTerrain=terrain??throw new ArgumentNullException(nameof(terrain));
            foreach(var region in chapterTerrain)art.ResolveRole(region.ArtRole);
        }
        int mapWidth, mapHeight, season, previousSeason, level;
        float visualTime, seasonAge, heroHitUntil, shakeUntil, levelAge = 10;
        public bool ShowAnnouncements { get; set; } = true;
        public bool ShowDamageNumbers { get; set; } = true;
        public bool ShakeEnabled { get; set; } = true;
        public Vector2 CameraShakeOffset { get; private set; }
        public string WarningSourceId { get; private set; } = "";
        public string WarningRank { get; private set; } = "";
        public float WarningRemainingSeconds { get; private set; }
        Func<PresentationEvent,bool> shouldDrawAttack;
        Action<PresentationEvent> onPresentationEvent;
        public int ActiveVisualProjectiles => ActivePersistentAttacks + effects.CountVisualProjectiles(shouldDrawAttack ??= ShouldDrawAttack);
        public int ActivePersistentAttacks
        {
            get { var count = wave == null ? 0 : wave.Projectiles.Count + wave.Attacks.Count; if (firstPlayable != null) for(var i=0;i<firstPlayable.Attacks.Count;i++) if (firstPlayable.Attacks[i].IsActive) count++; return count; }
        }
        public int ActiveEffects => effects.ActiveCount;
        public int DroppedEffects => effects.DroppedCount;
        public int UnsupportedShapeCount => effects.UnsupportedShapeCount;
        public int SubmittedInstances { get; private set; }
        public int DrawCalls { get; private set; }
        public Vector2? PredictedLordPosition { get; set; }
        public Vector2 RenderedLordPosition { get; private set; }
        public Action<string,int,Vector2> RenderedEntitySample { get; set; }
        readonly Dictionary<int,string> farmStageNames = new Dictionary<int,string>();
        readonly Dictionary<(string kind,int id),(ActorFacingMotion motion,int generation)> actorFacing = new Dictionary<(string,int),(ActorFacingMotion,int)>(1024);
        readonly List<(string kind,int id)> retiredFacing = new List<(string,int)>(1024);
        int facingGeneration;
        Vector2 previousRenderedLord;
        bool hasRenderedLord;

        public void Initialize(Camera camera, WorldCameraSettings cameraSettings, string estate, int width, int height)
        {
            if (art != null) throw new InvalidOperationException("World renderer is already initialized.");
            if (width <= 0 || height <= 0 || string.IsNullOrEmpty(estate)) throw new ArgumentException("Canonical map dimensions and estate ID are required.");
            shader = Resources.Load<Shader>("WorldSprite");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Instanced URP2D sprite shader is unavailable.");
            renderCamera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            settings = cameraSettings; followCamera = new RunCamera(camera, settings); followCamera.SetMapBounds(width, height);
            estateId = estate; mapWidth = width; mapHeight = height;
            art = ArtCatalog.Load(); numbers = new WorldDamageNumbers(transform); growthLabels = new WorldGrowthLabels(transform); announcements = new WorldAnnouncements(camera, art);
        }

        public void AcceptFrame(RunFrame frame, FirstPlayableFrame snapshot)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot), "The first-playable world requires its authoritative snapshot.");
            firstPlayable = snapshot;
            if (ReferenceEquals(frame, acceptedFrame)) return;
            if (acceptedFrame == null) { season = previousSeason = frame.Season; seasonAge = GameVisualTokens.SeasonBlendSeconds; level = frame.Level; }
            else
            {
                if (frame.Season != season) { previousSeason = season; season = frame.Season; seasonAge = 0; announcements.ShowSeason(season); }
                if (frame.Level > level) levelAge = 0;
                level = frame.Level;
            }
            acceptedFrame = frame;
            for(var i=0;i<frame.Farms.Count;i++)
            {
                var stage=frame.Farms[i].Stage;
                if(!farmStageNames.ContainsKey(stage))farmStageNames.Add(stage,"stage"+stage.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            construction.Clear(); foreach (var building in snapshot.BuildingProgress) construction.Add(building.Id, building);
            activities.Clear(); foreach (var person in snapshot.People) activities.Add(person.Id, person.Activity);
            activeAttackSources.Clear(); foreach (var attack in snapshot.Attacks) if (attack.IsActive) activeAttackSources.Add(attack.SourceId);
            effects.Accept(frame.Events, onPresentationEvent ??= OnEvent);
        }

        void OnEvent(PresentationEvent effect)
        {
            if (effect.Kind == PresentationKind.Attack)
                foreach (var id in effect.HitEntityIds) hitUntil[id] = visualTime + GameVisualTokens.HitFlashSeconds;
            if (effect.Kind == PresentationKind.LordHit)
            {
                heroHitUntil = visualTime + GameVisualTokens.HitFlashSeconds;
                shakeUntil = visualTime + GameVisualTokens.ShakeSeconds;
            }
            if (effect.Kind == PresentationKind.Damage && ShowDamageNumbers) numbers?.Add(Point(effect.Origin), effect.Amount);
            if (effect.Kind == PresentationKind.BossWarning)
            {
                foreach (var boss in firstPlayable.Bosses)
                    if (boss.DefinitionId == effect.SourceId) { WarningRank = boss.Rank; WarningSourceId = effect.SourceId; WarningRemainingSeconds = 1.2f; if (boss.Rank == "boss") announcements.ShowBoss(effect.SourceId); break; }
            }
        }

        public void Present(RunFrame previous, RunFrame current, FirstPlayableFrame snapshot, float alpha, float unscaledDeltaTime, Rect safeAreaPixels)
        {
            if (art == null) throw new InvalidOperationException("Initialize the world renderer before presenting frames.");
            if (current == null) throw new ArgumentNullException(nameof(current));
            previous = previous ?? current; alpha = Mathf.Clamp01(alpha);
            var seconds = Mathf.Max(0, unscaledDeltaTime);
            effects.Advance(seconds); visualTime += seconds; seasonAge += seconds; levelAge += seconds;
            WarningRemainingSeconds = Mathf.Max(0, WarningRemainingSeconds - seconds);
            numbers.Advance(seconds, ShowDamageNumbers); AcceptFrame(current, snapshot); IndexPrevious(previous);
            expiredHits.Clear(); foreach (var pair in hitUntil) if (pair.Value <= visualTime) expiredHits.Add(pair.Key);
            foreach (var id in expiredHits) hitUntil.Remove(id);
            var lord = PredictedLordPosition ?? Vector2.Lerp(Point(previous.Lord.Position), Point(current.Lord.Position), alpha);
            facingGeneration++;
            var heroMovement=hasRenderedLord?lord-previousRenderedLord:Point(current.Lord.Position)-Point(previous.Lord.Position);
            var heroFacing=Facing("lord",0,heroMovement);
            previousRenderedLord=lord;hasRenderedLord=true;
            RenderedLordPosition = lord;
            RenderedEntitySample?.Invoke("lord",0,lord);
            var shake = ShakeEnabled ? Mathf.Clamp01((shakeUntil - visualTime) / GameVisualTokens.ShakeSeconds) : 0;
            CameraShakeOffset = new Vector2(Mathf.Sin(visualTime * 113), Mathf.Cos(visualTime * 97)) * (shake * GameVisualTokens.ShakeAmplitude);
            if(wave!=null){var heroSize=Resolve("hero",snapshot.HeroId,"idle").WorldSize;followCamera.EdgeActorMargin=Mathf.Max(heroSize.x,heroSize.y)*GameVisualTokens.WaveEdgeActorMargin;}
            followCamera.LordViewportAnchor = FallowActive ? GameVisualTokens.FallowLordViewportAnchor : new Vector2(.5f, .5f);
            followCamera.PresentationZoom = FallowActive ? GameVisualTokens.FallowPresentationZoom : 1;
            followCamera.Present(lord, current.EstateExtent, seconds, safeAreaPixels); followCamera.SetVisualOffset(CameraShakeOffset);
            announcements.SetSuppressed(!ShowAnnouncements);
            announcements.Present(seconds, safeAreaPixels, renderCamera.WorldToScreenPoint(lord));
            foreach (var batch in batches.Values) batch.BeginFrame();
            lordMarker?.BeginFrame();
            UpdateFallowMask();
            DrawTerrain();
            DrawFallowLandmarks();
            DrawWave(current,alpha,lord);
            for(var i=0;i<current.Farms.Count;i++)
            {
                var farm=current.Farms[i];
                Draw(Resolve("crop", farm.SourceId, farmStageNames[farm.Stage]), farm.Ripe ? GameVisualTokens.ReadyLayer : GameVisualTokens.GrowthLayer, Point(farm.Position), visualTime + farm.Id * 0.13f);
            }
            for (var buildingIndex = 0; buildingIndex < current.Buildings.Count; buildingIndex++)
            {
                var building = current.Buildings[buildingIndex];
                if (!construction.TryGetValue(building.Id, out var progress)) throw new InvalidOperationException("Missing building progress: " + building.Id);
                var ready = progress.State == "complete" || progress.State == "damaged";
                var visual = Resolve("building", building.SourceId, progress.State);
                var time = visualTime + building.Id * 0.13f;
                Draw(visual, ready ? GameVisualTokens.ReadyLayer : GameVisualTokens.GrowthLayer, Point(building.Position), time);
                if (progress.State == "constructing")
                {
                    var fraction = Mathf.Clamp01((float)progress.Work / Mathf.Max(1, progress.RequiredWork));
                    Draw(Resolve("building", building.SourceId, "complete"), GameVisualTokens.GrowthLayer + 1, Point(building.Position), time, opacity: fraction * 0.65f);
                }
            }
            for(var i=0;i<current.Remains.Count;i++) Draw(Resolve("object", "remains", "default"), GameVisualTokens.GrowthLayer, Point(current.Remains[i].Position), visualTime);
            for(var i=0;i<current.Loot.Count;i++) Draw(Resolve("object", "loot", "default"), GameVisualTokens.ReadyLayer, Point(current.Loot[i].Position), visualTime);
            for (var mapEventIndex = 0; mapEventIndex < snapshot.MapEvents.Count; mapEventIndex++)
            {
                var mapEvent = snapshot.MapEvents[mapEventIndex];
                var state = mapEvent.MaxHealth > 0 && mapEvent.Health < mapEvent.MaxHealth ? "damaged" : "present";
                Draw(Resolve("mapEvent", mapEvent.DefinitionId, state), GameVisualTokens.ReadyLayer, Point(mapEvent.Position), visualTime);
            }
            for (var personIndex = 0; personIndex < current.People.Count; personIndex++)
            {
                var person = current.People[personIndex];
                if (!activities.TryGetValue(person.Id, out var activity)) throw new InvalidOperationException("Missing person activity: " + person.Id);
                Draw(Resolve("person", person.Role, activity), GameVisualTokens.AllyLayer, Interpolate(previousPeople, person.Id, person.Position, alpha), visualTime + person.Id * 0.13f);
            }
            DrawEffects(lord);
            for (var attackIndex = 0; attackIndex < snapshot.Attacks.Count; attackIndex++)
            {
                var attack = snapshot.Attacks[attackIndex];
                if (!attack.IsActive) continue;
                var before = Point(attack.PreviousPosition); var position = Vector2.Lerp(before, Point(attack.Position), alpha);
                var visual = Resolve("attack", attack.SourceId, attack.Form);
                var size = attack.Form == "field" || attack.Form == "nova" ? Vector2.one * (attack.PresentationRadius ?? attack.Radius) * 2 / settings.WorldUnitsPerUnityUnit : visual.WorldSize;
                Draw(visual, GameVisualTokens.AttackLayer, position, (float)attack.AgeTicks / current.TickRate, size, Angle(Point(attack.Position) - before), opacity: attack.Form == "field" ? GameVisualTokens.FieldOpacity : attack.Form == "nova" ? GameVisualTokens.AreaAttackOpacity : GameVisualTokens.TravellingAttackOpacity);
            }
            for (var enemyIndex = 0; enemyIndex < current.Enemies.Count; enemyIndex++)
            {
                var enemy = current.Enemies[enemyIndex];
                var flashing = hitUntil.ContainsKey(enemy.Id);
                var moved = previousEnemies.TryGetValue(enemy.Id, out var before) && before != Point(enemy.Position);
                var state = enemy.Health <= 0 ? "death" : flashing ? "hit" : moved ? "walk" : "idle";
                state = WaveEnemyState(enemy.Id, enemy.DefinitionId, state);
                var enemyArt=Resolve("enemy",enemy.DefinitionId,state);
                var position=Interpolate(previousEnemies,enemy.Id,enemy.Position,alpha);
                RenderedEntitySample?.Invoke("enemy",enemy.Id,position);
                var dimensions=enemyArt.WorldSize;
                var upperOpacity=1f;
                if(wave!=null)
                {
                    var kind=waveDefinition.Enemies[enemy.DefinitionId].Kind;
                    dimensions=WaveEnemySize(dimensions,kind);
                    var body=new Rect(position-Vector2.Scale(dimensions,enemyArt.Pivot),dimensions);
                    var warning=waveEnemyById.TryGetValue(enemy.Id,out var phase)&&
                        (phase.Phase.StartsWith("tell-",StringComparison.Ordinal)||phase.Phase=="charge"||phase.Phase=="water");
                    if(kind!=WaveEnemyKind.FloodBoss&&!warning&&!flashing&&body.Contains(lord))upperOpacity=GameVisualTokens.WaveEnemyUpperOpacity;
                }
                var hitProgress=flashing?1-(hitUntil[enemy.Id]-visualTime)/GameVisualTokens.HitFlashSeconds:-1;
                var enemyFacing=Facing("enemy",enemy.Id,moved?Point(enemy.Position)-before:Vector2.zero);
                DrawActor(enemyArt,GameVisualTokens.EnemyLayer+(wave==null?0:Mathf.Clamp(19-Mathf.FloorToInt(20f*enemy.Position.Y/mapHeight),0,19)),position,visualTime+enemy.Id*.13f,dimensions,moved,-1,hitProgress,flash:flashing ? .8f : 0,upperOpacity:upperOpacity,facing:enemyFacing);
            }
            var heroHit = heroHitUntil > visualTime;
            var heroState = current.Lord.Health <= 0 ? "death" : heroHit ? "hit" : Point(previous.Lord.Position) != Point(current.Lord.Position) ? "walk" : "idle";
            var heroWalking=Point(previous.Lord.Position)!=Point(current.Lord.Position);
            var heroAttackProgress=heroAttackUntil>visualTime?1-(heroAttackUntil-visualTime)/GameVisualTokens.ActorAttackSeconds:-1;
            var heroHitProgress=heroHit?1-(heroHitUntil-visualTime)/GameVisualTokens.HitFlashSeconds:-1;
            if(wave!=null)
            {
                var heroArt=Resolve("hero",snapshot.HeroId,heroState);
                if(lordMarker==null)
                {
                    lordMarkerMeshes=new ShapeMeshes();
                    lordMarker=new ShapeBatch(lordMarkerMeshes[WorldShape.Circle],Resources.Load<Shader>("WorldShape"),renderCamera,GameVisualTokens.LordLayer-2);
                    lordMarker.BeginFrame();
                }
                var radius=GameVisualTokens.WaveLordRingRadius;
                lordMarker.Add(lord,new Vector2(radius,radius*GameVisualTokens.WaveLordRingHeight),0,GameVisualTokens.Ink,GameVisualTokens.WaveLordRingInner);
                lordMarker.Add(lord,new Vector2(radius*GameVisualTokens.WaveLordRingAccentScale,radius*GameVisualTokens.WaveLordRingAccentScale*GameVisualTokens.WaveLordRingHeight),0,GameVisualTokens.Attack,GameVisualTokens.WaveLordRingAccentInner);
                foreach(var direction in HeroOutlineDirections)
                    DrawActor(heroArt,GameVisualTokens.LordLayer-1,lord+direction*GameVisualTokens.WaveHeroOutline,visualTime,heroArt.WorldSize,heroWalking,heroAttackProgress,heroHitProgress,tint:FallowActive ? GameVisualTokens.Ally : GameVisualTokens.Ink,facing:heroFacing);
            }
            var heroVisual=Resolve("hero",snapshot.HeroId,heroState);
            DrawActor(heroVisual,GameVisualTokens.LordLayer,lord,visualTime,heroVisual.WorldSize,heroWalking,heroAttackProgress,heroHitProgress,flash:heroHit ? .8f : 0,facing:heroFacing);
            if (levelAge < GameVisualTokens.EmphasisSeconds) Feedback("level-up", GameVisualTokens.ExperienceLayer, lord, levelAge / GameVisualTokens.EmphasisSeconds);
            threats.Update(renderCamera, current.Enemies, current.Lord.Position, settings.WorldUnitsPerUnityUnit, safeAreaPixels); DrawThreats();
            SubmittedInstances = 0; DrawCalls = 0;
            foreach (var batch in batches.Values) { batch.Flush(); SubmittedInstances += batch.SubmittedInstances; DrawCalls += batch.DrawCalls; }
            if(lordMarker!=null){lordMarker.Flush();SubmittedInstances+=lordMarker.SubmittedInstances;DrawCalls+=lordMarker.DrawCalls;}
            retiredFacing.Clear();
            foreach(var pair in actorFacing)if(pair.Value.generation!=facingGeneration)retiredFacing.Add(pair.Key);
            for(var i=0;i<retiredFacing.Count;i++)actorFacing.Remove(retiredFacing[i]);
        }
        public static Vector2 WaveEnemySize(Vector2 authored,WaveEnemyKind kind)
        {
            if(kind==WaveEnemyKind.FloodBoss)return authored;
            var size=authored*Mathf.Min(1,GameVisualTokens.WaveEnemyMaxSize/Mathf.Max(authored.x,authored.y));
            return kind==WaveEnemyKind.Ranged?size*GameVisualTokens.WaveWaspScale:size;
        }

        void IndexPrevious(RunFrame previous)
        {
            if (ReferenceEquals(indexedPrevious, previous)) return;
            indexedPrevious = previous; previousEnemies.Clear(); previousPeople.Clear();
            for(var i=0;i<previous.Enemies.Count;i++){var enemy=previous.Enemies[i];previousEnemies[enemy.Id]=Point(enemy.Position);}
            for(var i=0;i<previous.People.Count;i++){var person=previous.People[i];previousPeople[person.Id]=Point(person.Position);}
        }
        Vector2 Interpolate(Dictionary<int, Vector2> positions, int id, WorldPoint current, float alpha)
        {
            var point = Point(current); return positions.TryGetValue(id, out var before) ? Vector2.Lerp(before, point, alpha) : point;
        }
        Vector2 Point(WorldPoint value) => followCamera.Point(value);
        static float Angle(Vector2 direction) => Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        void DrawTerrain()
        {
            var width = (float)mapWidth / settings.WorldUnitsPerUnityUnit; var height = (float)mapHeight / settings.WorldUnitsPerUnityUnit;
            var visual = Resolve("terrain", estateId, GameVisualTokens.SeasonNames[season]);
            var old = Resolve("terrain", estateId, GameVisualTokens.SeasonNames[previousSeason]);
            if (FallowActive && TryFallowArt("fallow.terrain.ground", out var fallowGround)) visual = old = fallowGround;
            var blend = Mathf.Clamp01(seasonAge / GameVisualTokens.SeasonBlendSeconds);
            var tint = Color.Lerp(Color.white, Color.Lerp(GameVisualTokens.Seasons[previousSeason], GameVisualTokens.Seasons[season], blend), 0.16f);
            renderCamera.backgroundColor = Color.Lerp(GameVisualTokens.Seasons[previousSeason], GameVisualTokens.Seasons[season], blend);
            var terrainOpacity=wave==null?GameVisualTokens.TerrainOpacity:GameVisualTokens.WaveTerrainOpacity;
            if (FallowActive) { renderCamera.backgroundColor = GameVisualTokens.FallowAsh; terrainOpacity = 1; tint = Color.white; }
            var size = visual.WorldSize; var stride = size * GameVisualTokens.TerrainStride;
            var halfHeight = renderCamera.orthographicSize; var halfWidth = halfHeight * renderCamera.aspect; var center = renderCamera.transform.position;
            var xMin = Mathf.Max(-1, Mathf.FloorToInt((center.x - halfWidth) / stride.x) - 1); var xMax = Mathf.Min(Mathf.CeilToInt(width / stride.x), Mathf.FloorToInt((center.x + halfWidth) / stride.x) + 1);
            var yMin = Mathf.Max(-1, Mathf.FloorToInt((center.y - halfHeight) / stride.y) - 1); var yMax = Mathf.Min(Mathf.CeilToInt(height / stride.y), Mathf.FloorToInt((center.y + halfHeight) / stride.y) + 1);
            for (var y = yMin; y <= yMax; y++) for (var x = xMin; x <= xMax; x++)
            {
                var left = x * stride.x; var bottom = y * stride.y;
                var exterior=wave==null?0:followCamera.EdgeActorMargin;
                var x0 = Mathf.Max(-exterior, left); var y0 = Mathf.Max(-exterior, bottom);
                var x1 = Mathf.Min(width+exterior, left + size.x); var y1 = Mathf.Min(height+exterior, bottom + size.y);
                if (x1 <= x0 || y1 <= y0) continue;
                var cell = new Vector2(x1 - x0, y1 - y0);
                var position = new Vector2(x0, y0) + Vector2.Scale(visual.Pivot, cell);
                var fraction = new Rect((x0 - left) / size.x, (y0 - bottom) / size.y, cell.x / size.x, cell.y / size.y);
                if (blend < 1) Draw(old, GameVisualTokens.TerrainLayer, position, 0, cell, opacity: (1 - blend) * terrainOpacity, tint: tint, uv: TerrainUv(old.UvRects[0], fraction, (x & 1) != 0, (y & 1) != 0));
                Draw(visual, GameVisualTokens.TerrainLayer + 1, position, 0, cell, opacity: blend * terrainOpacity, tint: tint, uv: TerrainUv(visual.UvRects[0], fraction, (x & 1) != 0, (y & 1) != 0));
            }
            foreach(var region in chapterTerrain)
            {
                var patch=art.ResolveRole(region.ArtRole);
                var regionWidth=width*region.WidthPermille/1000f;var regionHeight=height*region.HeightPermille/1000f;
                var left=width*region.XPermille/1000f;var bottom=height*region.YPermille/1000f;
                var columns=Mathf.Max(1,Mathf.CeilToInt(regionWidth/patch.WorldSize.x));var rows=Mathf.Max(1,Mathf.CeilToInt(regionHeight/patch.WorldSize.y));
                var cell=new Vector2(regionWidth/columns,regionHeight/rows);
                for(var y=0;y<rows;y++)for(var x=0;x<columns;x++)
                {
                    var position=new Vector2(left+(x+.5f)*cell.x,bottom+(y+.5f)*cell.y);
                    if(position.x+cell.x<center.x-halfWidth||position.x-cell.x>center.x+halfWidth||position.y+cell.y<center.y-halfHeight||position.y-cell.y>center.y+halfHeight)continue;
                    Draw(patch,GameVisualTokens.TerrainLayer+2,position,0,cell,opacity:GameVisualTokens.TerrainOpacity);
                }
            }
            var edge = Resolve("boundary", "edge", "default"); var corner = Resolve("boundary", "corner", "default");
            if(wave!=null)
            {
                var columns=Mathf.CeilToInt(width/edge.WorldSize.x);var rows=Mathf.CeilToInt(height/edge.WorldSize.x);
                var horizontal=new Vector2(width/columns,edge.WorldSize.y);var vertical=new Vector2(height/rows,edge.WorldSize.y);
                for(var i=0;i<columns;i++)
                {
                    Draw(edge,GameVisualTokens.GrowthLayer-1,new Vector2((i+.5f)*horizontal.x,0),0,horizontal,opacity:GameVisualTokens.WaveFenceOpacity);
                    Draw(edge,GameVisualTokens.GrowthLayer-1,new Vector2((i+.5f)*horizontal.x,height),0,horizontal,180,GameVisualTokens.WaveFenceOpacity);
                }
                for(var i=0;i<rows;i++)
                {
                    Draw(edge,GameVisualTokens.GrowthLayer-1,new Vector2(0,(i+.5f)*vertical.x),0,vertical,90,GameVisualTokens.WaveFenceOpacity);
                    Draw(edge,GameVisualTokens.GrowthLayer-1,new Vector2(width,(i+.5f)*vertical.x),0,vertical,-90,GameVisualTokens.WaveFenceOpacity);
                }
                return;
            }
            var step = edge.WorldSize.x;
            for (var x = Mathf.Max(0, Mathf.FloorToInt((center.x - halfWidth) / step)); x * step < Mathf.Min(width, center.x + halfWidth + step); x++)
            {
                Draw(edge, GameVisualTokens.GrowthLayer - 1, new Vector2(Mathf.Min(width, (x + 0.5f) * step), 0), 0);
                Draw(edge, GameVisualTokens.GrowthLayer - 1, new Vector2(Mathf.Min(width, (x + 0.5f) * step), height), 0, degrees: 180);
            }
            for (var y = Mathf.Max(0, Mathf.FloorToInt((center.y - halfHeight) / step)); y * step < Mathf.Min(height, center.y + halfHeight + step); y++)
            {
                Draw(edge, GameVisualTokens.GrowthLayer - 1, new Vector2(0, Mathf.Min(height, (y + 0.5f) * step)), 0, degrees: 90);
                Draw(edge, GameVisualTokens.GrowthLayer - 1, new Vector2(width, Mathf.Min(height, (y + 0.5f) * step)), 0, degrees: -90);
            }
            Draw(corner, GameVisualTokens.GrowthLayer - 1, Vector2.zero, 0);
            Draw(corner, GameVisualTokens.GrowthLayer - 1, new Vector2(width, 0), 0, degrees: 90);
            Draw(corner, GameVisualTokens.GrowthLayer - 1, new Vector2(width, height), 0, degrees: 180);
            Draw(corner, GameVisualTokens.GrowthLayer - 1, new Vector2(0, height), 0, degrees: 270);
        }

        static Rect TerrainUv(Rect source, Rect fraction, bool flipX, bool flipY)
        {
            // Crop only the transparent decorative rim at draw time; preserve authored pixels and clip the sample with the map edge.
            var inset = GameVisualTokens.TerrainUvInset; var inner = 1 - inset * 2;
            var x = flipX ? 1 - fraction.x : fraction.x; var y = flipY ? 1 - fraction.y : fraction.y;
            return new Rect(source.x + source.width * (inset + x * inner), source.y + source.height * (inset + y * inner),
                source.width * fraction.width * inner * (flipX ? -1 : 1), source.height * fraction.height * inner * (flipY ? -1 : 1));
        }

        void DrawEffects(Vector2 lord)
        {
            for (var index = 0; index < effects.ActiveCount; index++)
            {
                var pooled = effects[index]; var effect = pooled.Event; var progress = pooled.Progress; var origin = Point(effect.Origin);
                switch (effect.Kind)
                {
                    case PresentationKind.Attack: DrawAttack(effect, pooled.Age, progress); break;
                    case PresentationKind.Damage: Feedback("hit", GameVisualTokens.AttackLayer, origin, progress); break;
                    case PresentationKind.EnemyKilled:
                        Draw(Resolve("enemy", effect.SourceId, "death"), GameVisualTokens.EnemyLayer, origin, pooled.Age, opacity: 1 - progress);
                        Feedback("death", GameVisualTokens.AttackLayer, origin, progress); break;
                    case PresentationKind.KillExperience: Feedback("kill-experience", GameVisualTokens.ExperienceLayer, Vector2.Lerp(origin, lord, progress * progress), progress); break;
                    case PresentationKind.HarvestExperience:
                        if(wave!=null)break;
                        Feedback("harvest", GameVisualTokens.ExperienceLayer, origin, progress);
                        Feedback("harvest-experience", GameVisualTokens.ExperienceLayer, Vector2.Lerp(origin, lord, progress * progress), progress); break;
                    case PresentationKind.TaxExperience: Feedback("tax", GameVisualTokens.ExperienceLayer, Vector2.Lerp(origin, lord, progress * progress), progress); break;
                    case PresentationKind.BuildingStarted: Feedback("building-start", GameVisualTokens.ReadyLayer, origin, progress); break;
                    case PresentationKind.BuildingCompleted: Feedback("building-complete", GameVisualTokens.ReadyLayer, origin, progress); break;
                    case PresentationKind.Evolution: Feedback("evolution", GameVisualTokens.ExperienceLayer, origin, progress); break;
                    case PresentationKind.LordHit: Feedback("lord-hit", GameVisualTokens.AttackLayer, origin, progress); break;
                    case PresentationKind.EventSpawned: Feedback("event-spawn", GameVisualTokens.ReadyLayer, origin, progress); break;
                    case PresentationKind.EventClaimed:
                        Draw(Resolve("mapEvent", effect.SourceId, "claimed"), GameVisualTokens.ReadyLayer, origin, pooled.Age, opacity: 1 - progress);
                        Feedback("event-claim", GameVisualTokens.ReadyLayer, origin, progress); break;
                    case PresentationKind.CartBroken:
                        Draw(Resolve("mapEvent", effect.SourceId, "broken"), GameVisualTokens.ReadyLayer, origin, pooled.Age, opacity: 1 - progress);
                        Feedback("cart-broken", GameVisualTokens.ReadyLayer, origin, progress); break;
                    case PresentationKind.BossWarning:
                        foreach (var boss in firstPlayable.Bosses) if (boss.DefinitionId == effect.SourceId) { Feedback(boss.Rank == "boss" ? "boss-warning" : "elite-warning", GameVisualTokens.ThreatLayer, origin, progress); break; }
                        break;
                }
            }
        }
        bool ShouldDrawAttack(PresentationEvent effect) => !(activeAttackSources.Contains(effect.SourceId) && (WorldEffects.IsTravelling(effect.Shape) || effect.Shape == "field" || effect.Shape == "nova"));
        void DrawAttack(PresentationEvent effect, float age, float progress)
        {
            if (!ShouldDrawAttack(effect)) return;
            var visual = Resolve("attack", effect.SourceId, effect.Shape); var origin = Point(effect.Origin);
            if (effect.Shape == "rays" || effect.Shape == "projectile" || effect.Shape == "chain")
            {
                var ends = effect.Shape == "chain" ? effect.Endpoints : effect.Geometry?.RayEnds ?? effect.Endpoints;
                for(var endIndex=0;endIndex<ends.Count;endIndex++)
                {
                    var end=ends[endIndex];
                    var target = Point(end); var vector = target - origin;
                    Draw(visual, GameVisualTokens.AttackLayer, (origin + target) * 0.5f, age, new Vector2(vector.magnitude, Mathf.Min(visual.WorldSize.y, GameVisualTokens.AttackRibbonWidth)), Angle(vector), (1 - progress) * GameVisualTokens.TravellingAttackOpacity);
                    if (effect.Shape == "chain") origin = target;
                }
                if (ends.Count > 0) return;
            }
            var area = IsAreaAttack(effect.Shape);
            var size = EventAttackSize(effect.Shape, effect.Range, visual.WorldSize, settings.WorldUnitsPerUnityUnit);
            Draw(visual, GameVisualTokens.AttackLayer, origin, age, size, Angle(new Vector2(effect.Direction.X, effect.Direction.Y)), (1 - progress) * (area ? GameVisualTokens.AreaAttackOpacity : GameVisualTokens.TravellingAttackOpacity));
        }
        static bool IsAreaAttack(string shape) => shape == "sector90" || shape == "sector180" || shape == "disk" || shape == "nova" || shape == "wave" || shape == "melee" || shape == "orbit";
        public static Vector2 EventAttackSize(string shape, int range, Vector2 authoredSize, float worldUnitsPerUnityUnit) =>
            IsAreaAttack(shape) ? Vector2.one * range * 2 / worldUnitsPerUnityUnit : authoredSize;

        void Feedback(string name,int layer,Vector2 point,float progress)
        {
            var visual=Resolve("feedback",name,"default");
            var experience=wave!=null&&(name=="kill-experience"||name=="harvest-experience");
            Draw(visual,layer,point,experience?0:progress,experience?visual.WorldSize*GameVisualTokens.WaveRewardScale:(Vector2?)null,opacity:(1-progress)*(experience?GameVisualTokens.WaveRewardOpacity:1));
        }
        void DrawThreats()
        {
            var pixelsToWorld = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            for (var i = 0; i < threats.Count; i++)
            {
                var marker = threats[i]; var point = (Vector2)renderCamera.ScreenToWorldPoint(new Vector3(marker.ScreenPosition.x, marker.ScreenPosition.y, -renderCamera.transform.position.z));
                Draw(Resolve("feedback", "threat", "default"), GameVisualTokens.ThreatLayer, point, visualTime, Vector2.one * pixelsToWorld * 24, Angle(marker.Direction));
                if (marker.ClusterCount > 1) Draw(Resolve("feedback", "threat-cluster", "default"), GameVisualTokens.ThreatLayer, point - marker.Direction * pixelsToWorld * 18, visualTime, Vector2.one * pixelsToWorld * 12);
            }
        }
        ArtVisual Resolve(string kind, string id, string state)
        {
            var key = (kind, id, state);
            if (!visuals.TryGetValue(key, out var visual)) { visual = art.Resolve(kind, id, state); visuals.Add(key, visual); }
            return visual;
        }
        ActorFacingPose Facing(string kind,int id,Vector2 movement)
        {
            var key=(kind,id);
            if(!actorFacing.TryGetValue(key,out var state))state=(new ActorFacingMotion(movement,visualTime),facingGeneration);
            state.motion.SetDirection(movement,visualTime);state.generation=facingGeneration;actorFacing[key]=state;
            return state.motion.Sample(visualTime);
        }
        void DrawActor(ArtVisual visual,int layer,Vector2 position,float time,Vector2 size,bool walking,float attackProgress,float hitProgress,Color? tint=null,float flash=0,float upperOpacity=1,ActorFacingPose facing=default)
        {
            var pose=ActorMotion.Sample(time,walking,attackProgress,hitProgress);
            var dimensions=Vector2.Scale(size,pose.Scale);
            var angle=pose.Degrees+facing.LeanDegrees;
            dimensions.x *= facing.SignedWidth;
            Draw(visual,layer,position+pose.Offset,time,dimensions,angle,tint:tint,flash:flash,upperOpacity:upperOpacity,applyTween:false);
        }
        void Draw(ArtVisual visual, int layer, Vector2 position, float time, Vector2? size = null, float degrees = 0, float opacity = 1, Color? tint = null, float flash = 0, Rect? uv = null, float edgeTexels = 0, float upperOpacity = 1, bool applyTween = true)
        {
            var dimensions = size ?? visual.WorldSize;
            var phase = time * Mathf.PI * 2 / GameVisualTokens.WalkSeconds;
            switch (applyTween ? visual.Tween : "none")
            {
                case "walk": position.y += Mathf.Abs(Mathf.Sin(phase)) * GameVisualTokens.WalkBob; degrees += Mathf.Sin(phase) * GameVisualTokens.WalkLeanDegrees; break;
                case "work": degrees += Mathf.Sin(phase) * GameVisualTokens.WalkLeanDegrees * 1.5f; break;
                case "pulse": dimensions *= 1 + 0.035f * Mathf.Sin(phase * 0.4f); break;
                case "recoil": dimensions.y *= 1 - flash * 0.07f; break;
            }
            var frame = Mathf.FloorToInt(Mathf.Max(0, time) * 1000 / visual.FrameMs) % visual.UvRects.Count;
            var color = tint ?? Color.white; color.a *= Mathf.Clamp01(opacity);
            var key = (layer, visual.Texture);
            if (!batches.TryGetValue(key, out var batch))
            {
                var surfaceShader = FallowActive && layer < GameVisualTokens.AllyLayer && layer != GameVisualTokens.ExperienceLayer
                    ? layer <= GameVisualTokens.TerrainLayer + 1 ? fallowGroundShader : fallowShader : shader;
                batch = new SpriteBatch(visual.Texture, surfaceShader, renderCamera, layer); batches.Add(key, batch);
            }
            batch.Add(position, dimensions, visual.Pivot, uv ?? visual.UvRects[frame], degrees, color, flash, edgeTexels, upperOpacity);
        }
        void OnDestroy()
        {
            DisposeFallow();
            foreach (var batch in batches.Values) batch.Dispose(); batches.Clear(); lordMarker?.Dispose();lordMarkerMeshes?.Dispose(); visuals.Clear(); numbers?.Dispose(); growthLabels?.Dispose(); announcements?.Dispose(); art = null;
        }
    }
}
