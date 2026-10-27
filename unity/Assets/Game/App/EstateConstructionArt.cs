using System;
using System.Collections.Generic;
using System.Linq;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    // Approved art #426: purely visual intervals; never changes the construction clock or saved state.
    public static class EstateConstructionArt
    {
        public static int Stage(MetaEstateWork work)
        {
            if (work == null) return 0;
            var elapsed = (decimal)work.TotalSeconds - work.RemainingSeconds;
            if (work.TotalSeconds <= 0) return 3;
            return elapsed * 100 < work.TotalSeconds * 33m ? 1 : elapsed * 100 < work.TotalSeconds * 66m ? 2 : 3;
        }

        public static string Role(bool field, int stage, bool lit) =>
            "construction-" + (field ? "field" : "building") + "-" + stage + (lit ? "-lit" : "-dark");

        public static float CappedWidth(EstateArt art, string role, string kind, int completedStage, float completedWidth)
        {
            var completedHeight = art.BodyHeight(kind, completedStage) * completedWidth / art.BuildingCanvas.x;
            var originalHeight = art.OpaqueHeight(role) * 136 / art.Get(role).rect.width;
            return 136 * Mathf.Min(1, completedHeight / originalHeight);
        }

        // Snap each doorway to the existing road graph, then use its stable BFS. A missing route draws no worker.
        public static Vector2[] Route(EstatePlan plan, Vector2Int from, Vector2Int to)
        {
            if (plan.Road.Count == 0 || from == to) return Array.Empty<Vector2>();
            Vector2Int Nearest(Vector2Int target) => plan.Road.OrderBy(c => Mathf.Abs(c.x - target.x) + Mathf.Abs(c.y - target.y))
                .ThenBy(c => c.x).ThenBy(c => c.y).First();
            var cells = EstatePlanner.RoadPath(Nearest(from), Nearest(to), plan.Road.Contains);
            return cells.Count < 2 ? Array.Empty<Vector2>() : cells.Select(c => (Vector2)c + new Vector2(.5f, .5f)).ToArray();
        }
    }

    // Cached projected distances give constant walking speed on both estate axes, independent of zoom.
    public sealed class EstateCarryRoute
    {
        public const float PixelsPerSecond = 26;
        public readonly Vector2[] Points;
        readonly float[] distances;
        public float Length => distances.Length == 0 ? 0 : distances[distances.Length - 1];
        public EstateCarryRoute(Vector2[] points)
        {
            Points = (Vector2[])points.Clone(); distances = new float[points.Length];
            for (var i = 1; i < points.Length; i++)
                distances[i] = distances[i - 1] + Vector2.Distance(EstateMapModel.Project(points[i - 1]), EstateMapModel.Project(points[i]));
        }
        public (Vector2 Grid, bool East, int Frame) Sample(double seconds)
        {
            if (Points.Length < 2 || Length <= 0) return (Points.Length == 0 ? Vector2.zero : Points[0], true, 0);
            var travel = (float)(Math.Max(0, seconds) * PixelsPerSecond % (Length * 2));
            var returning = travel > Length; var distance = returning ? Length * 2 - travel : travel;
            var end = 1; while (end < distances.Length - 1 && distances[end] < distance) end++;
            var fraction = Mathf.InverseLerp(distances[end - 1], distances[end], distance);
            var delta = EstateMapModel.Project(Points[end]) - EstateMapModel.Project(Points[end - 1]);
            if (returning) delta = -delta;
            // Only approved SE/SW art exists. Uphill legs keep that horizontal facing; no invented rear frames.
            return (Vector2.Lerp(Points[end - 1], Points[end], fraction), delta.x >= 0, (int)(Math.Max(0, seconds) / .05 % 16));
        }
    }
}
