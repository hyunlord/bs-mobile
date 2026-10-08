using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public readonly struct ThreatMarker
    {
        public readonly Vector2 ScreenPosition;
        public readonly Vector2 Direction;
        public readonly int ClusterCount;
        internal ThreatMarker(Vector2 screenPosition, Vector2 direction, int clusterCount) { ScreenPosition = screenPosition; Direction = direction; ClusterCount = clusterCount; }
    }

    public sealed class ThreatMarkers
    {
        public const int MaximumMarkers = 8;
        private const float EdgePaddingPixels = 22;
        private readonly ThreatMarker[] markers = new ThreatMarker[MaximumMarkers];
        private readonly float[] distances = new float[MaximumMarkers];
        private readonly Vector2[] directions = new Vector2[MaximumMarkers];
        private readonly int[] clusters = new int[MaximumMarkers];
        public int Count { get; private set; }
        public ThreatMarker this[int index] => markers[index];

        public void Update(Camera camera, IReadOnlyList<EnemyView> enemies, WorldPoint lord, int units, Rect safeArea)
        {
            Count = 0;
            for (var index = 0; index < MaximumMarkers; index++) { distances[index] = float.PositiveInfinity; clusters[index] = 0; }
            var bounds = new Rect(Mathf.Max(camera.pixelRect.xMin, safeArea.xMin), Mathf.Max(camera.pixelRect.yMin, safeArea.yMin), 0, 0);
            bounds.xMax = Mathf.Min(camera.pixelRect.xMax, safeArea.xMax); bounds.yMax = Mathf.Min(camera.pixelRect.yMax, safeArea.yMax);
            if (bounds.width <= EdgePaddingPixels * 2 || bounds.height <= EdgePaddingPixels * 2) return;
            var visibleBounds = bounds;
            bounds.xMin += EdgePaddingPixels; bounds.xMax -= EdgePaddingPixels; bounds.yMin += EdgePaddingPixels; bounds.yMax -= EdgePaddingPixels;
            var center = bounds.center;
            foreach (var enemy in enemies)
            {
                var point = camera.WorldToScreenPoint(new Vector3((float)enemy.Position.X / units, (float)enemy.Position.Y / units, 0));
                if (visibleBounds.Contains(point) && point.z > 0) continue;
                var direction = ((Vector2)point - center).normalized;
                if (point.z < 0) direction = -direction;
                if (direction.sqrMagnitude == 0) continue;
                var angle = Mathf.Atan2(direction.y, direction.x) + Mathf.PI;
                var sector = Mathf.FloorToInt((angle / (Mathf.PI * 2) * MaximumMarkers) + 0.5f) % MaximumMarkers;
                clusters[sector]++;
                var dx = (float)enemy.Position.X - lord.X; var dy = (float)enemy.Position.Y - lord.Y;
                var distance = dx * dx + dy * dy;
                if (distance >= distances[sector]) continue;
                distances[sector] = distance; directions[sector] = direction;
            }
            for (var index = 0; index < MaximumMarkers; index++)
            {
                if (clusters[index] == 0) continue;
                var direction = directions[index];
                var edge = Mathf.Min(Mathf.Abs(direction.x) > 0.001f ? bounds.width * 0.5f / Mathf.Abs(direction.x) : float.PositiveInfinity,
                    Mathf.Abs(direction.y) > 0.001f ? bounds.height * 0.5f / Mathf.Abs(direction.y) : float.PositiveInfinity);
                markers[Count++] = new ThreatMarker(center + direction * edge, direction, clusters[index]);
            }
        }
    }
}
