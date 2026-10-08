using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed class WorldRenderer : MonoBehaviour
    {
        public const float LordRadius = 0.13f;
        public const float EnemyRadius = 0.11f;
        public const float PersonRadius = 0.085f;
        public const float FarmRadius = 0.16f;
        public const float BuildingRadius = 0.19f;
        private const float AllyInnerScale = 0.72f;
        private const float ReadyPulseAmplitude = 0.045f;
        private const float ReadyPulseRate = 2;
        private const int GrowthLayer = 10;
        private const int ReadyLayer = 20;
        private const int AllyOutlineLayer = 30;
        private const int AllyFillLayer = 31;
        private const int AttackLayer = 40;
        private const int EnemyLayer = 50;
        private const int LordOutlineLayer = 80;
        private const int ExperienceLayer = 70;
        private const int ThreatLayer = 90;
        private readonly Dictionary<int, ShapeBatch> batches = new Dictionary<int, ShapeBatch>();
        private readonly Dictionary<int, Vector2> previousEnemies = new Dictionary<int, Vector2>();
        private readonly Dictionary<int, Vector2> previousPeople = new Dictionary<int, Vector2>();
        private readonly WorldEffects effects = new WorldEffects();
        private readonly ThreatMarkers threats = new ThreatMarkers();
        private RunFrame indexedPrevious;
        private Camera renderCamera;
        private RunCamera followCamera;
        private WorldCameraSettings settings;
        private ShapeMeshes meshes;
        private Shader shader;
        private float visualTime;
        public int ActiveVisualProjectiles => effects.ActiveVisualProjectiles;
        public int ActiveEffects => effects.ActiveCount;
        public int DroppedEffects => effects.DroppedCount;
        public int UnsupportedShapeCount => effects.UnsupportedShapeCount;
        public int SubmittedInstances { get; private set; }
        public int DrawCalls { get; private set; }

        public void Initialize(Camera camera, WorldCameraSettings cameraSettings)
        {
            if (meshes != null) throw new InvalidOperationException("World renderer is already initialized.");
            shader = Resources.Load<Shader>("WorldShape");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Instanced URP2D world shader is unavailable.");
            if (!SystemInfo.supportsInstancing) throw new InvalidOperationException("GPU instancing is required by the world renderer.");
            renderCamera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            settings = cameraSettings; followCamera = new RunCamera(camera, settings); meshes = new ShapeMeshes();
        }

        public void AcceptFrame(RunFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            effects.Accept(frame.Events);
        }

        public void Present(RunFrame previous, RunFrame current, float alpha, float unscaledDeltaTime, Rect safeAreaPixels)
        {
            if (meshes == null) throw new InvalidOperationException("Initialize the world renderer before presenting frames.");
            if (current == null) throw new ArgumentNullException(nameof(current));
            previous = previous ?? current; alpha = Mathf.Clamp01(alpha);
            effects.Advance(unscaledDeltaTime); AcceptFrame(current); visualTime += Mathf.Max(0, unscaledDeltaTime);
            IndexPrevious(previous);
            var lord = Vector2.Lerp(Point(previous.Lord.Position), Point(current.Lord.Position), alpha);
            followCamera.Present(lord, current.EstateExtent, unscaledDeltaTime);
            foreach (var batch in batches.Values) batch.BeginFrame();
            DrawEstate(current);
            var pulse = 1 + ReadyPulseAmplitude * Mathf.Sin(visualTime * ReadyPulseRate);
            foreach (var farm in current.Farms)
                Add(WorldShape.Square, farm.Ripe ? ReadyLayer : GrowthLayer, Point(farm.Position), Vector2.one * FarmRadius * (farm.Ripe ? pulse : 1), 0, farm.Ripe ? GamePalette.Ready : GamePalette.Growing);
            foreach (var building in current.Buildings)
            {
                var ready = building.Built && building.Health > 0;
                Add(WorldShape.Square, ready ? ReadyLayer : GrowthLayer, Point(building.Position), Vector2.one * BuildingRadius * (ready ? pulse : 1), 45, ready ? GamePalette.Ready : GamePalette.Growing);
                if (building.Built && building.Health <= 0) Add(WorldShape.Square, GrowthLayer + 1, Point(building.Position), new Vector2(BuildingRadius, 0.018f), -45, GamePalette.Stroke);
            }
            foreach (var remain in current.Remains) Add(WorldShape.Diamond, GrowthLayer, Point(remain.Position), Vector2.one * 0.035f, 0, GamePalette.Stroke);
            foreach (var loot in current.Loot) Add(WorldShape.Diamond, GrowthLayer, Point(loot.Position), Vector2.one * 0.06f, 0, GamePalette.Growing);
            foreach (var person in current.People)
            {
                var point = Interpolate(previousPeople, person.Id, person.Position, alpha);
                var shape = person.Role == "militia" ? WorldShape.Triangle : person.Role == "vassal" ? WorldShape.Diamond : WorldShape.Circle;
                Add(shape, AllyOutlineLayer, point, Vector2.one * PersonRadius, 0, GamePalette.Lord);
                Add(shape, AllyFillLayer, point, Vector2.one * PersonRadius * AllyInnerScale, 0, GamePalette.AllyFill);
            }
            DrawEffects();
            foreach (var enemy in current.Enemies)
                Add(WorldShape.Diamond, EnemyLayer, Interpolate(previousEnemies, enemy.Id, enemy.Position, alpha), Vector2.one * EnemyRadius, 0, GamePalette.Enemy);
            Add(WorldShape.Circle, LordOutlineLayer, lord, Vector2.one * LordRadius, 0, GamePalette.Lord);
            var facing = new Vector2(current.Lord.Facing.X, current.Lord.Facing.Y).normalized;
            Add(WorldShape.Triangle, LordOutlineLayer + 1, lord + facing * LordRadius, Vector2.one * 0.045f, Angle(facing), GamePalette.Lord);
            threats.Update(renderCamera, current.Enemies, current.Lord.Position, settings.WorldUnitsPerUnityUnit, safeAreaPixels);
            DrawThreats();
            SubmittedInstances = 0; DrawCalls = 0;
            foreach (var batch in batches.Values) { batch.Flush(); SubmittedInstances += batch.SubmittedInstances; DrawCalls += batch.DrawCalls; }
        }

        private void IndexPrevious(RunFrame previous)
        {
            if (ReferenceEquals(indexedPrevious, previous)) return;
            indexedPrevious = previous; previousEnemies.Clear(); previousPeople.Clear();
            foreach (var enemy in previous.Enemies) previousEnemies[enemy.Id] = Point(enemy.Position);
            foreach (var person in previous.People) previousPeople[person.Id] = Point(person.Position);
        }

        private Vector2 Interpolate(Dictionary<int, Vector2> positions, int id, WorldPoint current, float alpha)
        {
            var point = Point(current); return positions.TryGetValue(id, out var before) ? Vector2.Lerp(before, point, alpha) : point;
        }
        private Vector2 Point(WorldPoint value) => followCamera.Point(value);
        private static float Angle(Vector2 direction) => Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        private static Color Fade(Color color, float opacity) { color.a *= Mathf.Clamp01(opacity); return color; }

        private void DrawEstate(RunFrame frame)
        {
            var extent = (float)frame.EstateExtent / settings.WorldUnitsPerUnityUnit;
            Add(WorldShape.Circle, GrowthLayer - 1, Point(frame.Estate), Vector2.one * extent, 0, Fade(GamePalette.Growing, 0.25f), 0.99f);
        }

        private void DrawEffects()
        {
            for (var index = 0; index < effects.ActiveCount; index++)
            {
                var pooled = effects[index]; var effect = pooled.Event; var progress = pooled.Progress;
                var origin = Point(effect.Origin);
                if (effect.Kind == PresentationKind.Attack) { DrawAttack(effect, progress); continue; }
                if (effect.Kind == PresentationKind.BuildingCompleted)
                {
                    Add(WorldShape.Circle, ReadyLayer + 1, origin, Vector2.one * BuildingRadius * (1 + progress), 0, Fade(GamePalette.Ready, 1 - progress), 0.88f); continue;
                }
                var color = effect.Kind == PresentationKind.KillExperience ? GamePalette.KillXp : effect.Kind == PresentationKind.HarvestExperience ? GamePalette.HarvestXp : Fade(GamePalette.MutedText, 0.5f);
                var shape = effect.Kind == PresentationKind.KillExperience ? WorldShape.Diamond : effect.Kind == PresentationKind.HarvestExperience ? WorldShape.Circle : WorldShape.Square;
                Add(shape, ExperienceLayer, origin + Vector2.up * (progress * 0.22f), Vector2.one * 0.045f, 0, Fade(color, 1 - progress));
            }
        }

        private void DrawAttack(PresentationEvent effect, float progress)
        {
            var origin = Point(effect.Origin); var radius = (float)effect.Range / settings.WorldUnitsPerUnityUnit;
            var color = Fade(GamePalette.Attack, 1 - progress); var direction = new Vector2(effect.Direction.X, effect.Direction.Y);
            if (effect.Shape == "rays" || effect.Shape == "projectile")
            {
                if (effect.Geometry == null) return;
                foreach (var end in effect.Geometry.RayEnds)
                {
                    var target = Point(end); var vector = target - origin;
                    var halfWidth = Mathf.Max(0.012f, (float)effect.Geometry.BeamHalfWidth / settings.WorldUnitsPerUnityUnit);
                    Add(WorldShape.Square, AttackLayer, (origin + target) * 0.5f, new Vector2(vector.magnitude * 0.5f, halfWidth), Angle(vector), color);
                    Add(WorldShape.Circle, AttackLayer, Vector2.Lerp(origin, target, progress), Vector2.one * halfWidth * 1.5f, 0, color);
                }
                return;
            }
            var shape = effect.Shape == "sector90" ? WorldShape.Sector90 : effect.Shape == "sector180" || effect.Shape == "melee" ? WorldShape.Sector180 : WorldShape.Circle;
            var inner = effect.Shape == "orbit" && effect.Geometry != null && effect.Range > 0 ? (float)effect.Geometry.InnerRadius / effect.Range : -1;
            Add(shape, AttackLayer, origin, Vector2.one * radius, Angle(direction), color, inner);
        }

        private void DrawThreats()
        {
            var pixelsToWorld = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            for (var index = 0; index < threats.Count; index++)
            {
                var marker = threats[index]; var point = renderCamera.ScreenToWorldPoint(new Vector3(marker.ScreenPosition.x, marker.ScreenPosition.y, -renderCamera.transform.position.z));
                Add(WorldShape.Triangle, ThreatLayer, point, Vector2.one * pixelsToWorld * 7, Angle(marker.Direction), GamePalette.Enemy);
                if (marker.ClusterCount > 1)
                    Add(WorldShape.Circle, ThreatLayer, (Vector2)point - marker.Direction * pixelsToWorld * 12, Vector2.one * pixelsToWorld * 2.5f, 0, GamePalette.Enemy);
            }
        }

        private void Add(WorldShape shape, int layer, Vector2 position, Vector2 scale, float degrees, Color color, float innerRadius = -1)
        {
            var key = layer * 10 + (int)shape;
            if (!batches.TryGetValue(key, out var batch)) { batch = new ShapeBatch(meshes[shape], shader, renderCamera, layer); batches.Add(key, batch); }
            batch.Add(position, scale, degrees, color, innerRadius);
        }

        private void OnDestroy()
        {
            foreach (var batch in batches.Values) batch.Dispose(); batches.Clear(); meshes?.Dispose(); meshes = null;
        }
    }
}
