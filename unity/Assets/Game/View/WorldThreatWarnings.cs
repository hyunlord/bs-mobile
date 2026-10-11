using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        readonly List<ThreatCorridor> warningSelection = new List<ThreatCorridor>(3);
        readonly HashSet<(int, int)> explainedCharges = new HashSet<(int, int)>(2);
        ShapeMeshes warningMeshes;
        ShapeBatch warningLines, warningLineBacking, warningArrowBacking, warningArrows;
        WorldGrowthLabels warningLabels;
        float warningCaptionUntil;

        void InitializeThreatWarnings()
        {
            if (warningLines == null)
            {
                warningMeshes = new ShapeMeshes();
                var shader = Resources.Load<Shader>("WorldShape");
                warningLines = new ShapeBatch(warningMeshes[WorldShape.Square], shader, renderCamera, GameVisualTokens.WaveDangerLayer);
                warningLineBacking = new ShapeBatch(warningMeshes[WorldShape.Square], shader, renderCamera, GameVisualTokens.WaveDangerLayer - 1);
                warningArrowBacking = new ShapeBatch(warningMeshes[WorldShape.Triangle], shader, renderCamera, GameVisualTokens.ThreatLayer - 1);
                warningArrows = new ShapeBatch(warningMeshes[WorldShape.Triangle], shader, renderCamera, GameVisualTokens.ThreatLayer);
                warningLabels = new WorldGrowthLabels(transform, "돌진 — 옆으로 피하세요", GameVisualTokens.Hostile);
                warningLabels.Show(0, "돌진 — 옆으로 피하세요", Vector2.zero);
                warningLabels.Clear();
            }
        }

        void DrawThreatWarnings(RunFrame current, float alpha, Vector2 lord)
        {
            warningLines.BeginFrame(); warningLineBacking.BeginFrame(); warningArrowBacking.BeginFrame(); warningArrows.BeginFrame(); warningLabels.BeginFrame(); warningSelection.Clear();
            var halfHeight = renderCamera.orthographicSize;
            var visible = OverlayWorldBounds();
            warningLines.SetClipRect(visible); warningLineBacking.SetClipRect(visible);
            warningArrowBacking.SetClipRect(visible); warningArrows.SetClipRect(visible);
            for (var index = 0; index < wave.Enemies.Count; index++)
            {
                var enemy = wave.Enemies[index];
                if (enemy.Phase != "tell-charge" && enemy.Phase != "tell-water" && enemy.Phase != "water") continue;
                if (!waveActorById.TryGetValue(enemy.Id, out var actor) || actor.Health <= 0) continue;
                var origin = Point(enemy.Origin);
                var water = enemy.Phase != "tell-charge";
                var lane = new ThreatCorridor { Id = enemy.Id, ImpactTick = enemy.UntilTick,
                    TellTicks = waveDefinition.Enemies[actor.DefinitionId].TellTicks, Origin = origin, Target = Point(enemy.Target),
                    Radius = waveCatalog.Enemies[actor.DefinitionId].Range / (float)settings.WorldUnitsPerUnityUnit,
                    Water = water, Active = enemy.Phase == "water" };
                if (water && !ThreatCorridors.Contains(lane, lord)) continue;
                if (!ThreatCorridors.Intersects(visible, lane)) continue;
                if ((lane.Target - lane.Origin).sqrMagnitude > .0001f) ThreatCorridors.Consider(lane, warningSelection);
            }
            var pixelsToWorld = halfHeight * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            for (var index = 0; index < warningSelection.Count; index++)
            {
                var lane = warningSelection[index];
                var vector = lane.Target - lane.Origin; var length = vector.magnitude; var axis = vector / length;
                ThreatCorridors.LocalSegment(lane, lord, halfHeight * 2 * GameVisualTokens.ThreatStripHeightFraction, out var coveredStart, out var coveredEnd);
                var normal = new Vector2(-axis.y, axis.x); var angle = Angle(axis);
                var color = lane.Water ? Color.Lerp(GameVisualTokens.Hostile, GameVisualTokens.Ally, .25f) : GameVisualTokens.Hostile;
                var progress = ThreatCorridors.Progress(lane, current.Tick, alpha);
                DrawWarningStrip(lane, coveredStart, coveredEnd, normal, angle, color, progress, pixelsToWorld);
                var arrowLength = Mathf.Min(length * .22f, pixelsToWorld * GameVisualTokens.ThreatArrowLengthPixels);
                var arrowWidth = Mathf.Min(lane.Radius * .7f, pixelsToWorld * GameVisualTokens.ThreatArrowWidthPixels);
                var backing = pixelsToWorld * GameVisualTokens.ThreatArrowBackingPixels;
                var arrowLane = lane; arrowLane.Origin = coveredStart; arrowLane.Target = coveredEnd;
                var arrowPosition = ThreatCorridors.OriginVisible(visible, lane.Origin)
                    ? ThreatCorridors.VisibleArrowPosition(visible, arrowLane, arrowLength + backing)
                    : ThreatCorridors.EntryArrowPosition(visible, lane, arrowLength + backing);
                warningArrowBacking.Add(arrowPosition, new Vector2(arrowLength + backing, arrowWidth + backing), angle, GameVisualTokens.Ink);
                warningArrows.Add(arrowPosition, new Vector2(arrowLength, arrowWidth), angle, color);
                if (!lane.Water && explainedCharges.Count < 2 && explainedCharges.Add((lane.Id, lane.ImpactTick))) warningCaptionUntil = visualTime + 2;
            }
            if (visualTime < warningCaptionUntil)
                warningLabels.ShowClamped(0, "돌진 — 옆으로 피하세요", lord + Vector2.up * .75f, visible);
            warningLineBacking.Flush(); warningLines.Flush(); warningArrowBacking.Flush(); warningArrows.Flush();
        }

        void DrawWarningStrip(ThreatCorridor lane, Vector2 start, Vector2 end, Vector2 normal, float angle, Color color, float progress, float pixelsToWorld)
        {
            var delta = end - start;
            var length = delta.magnitude;
            var steps = lane.Water ? 1 : GameVisualTokens.ThreatFadeSteps;
            var lineWidth = pixelsToWorld * GameVisualTokens.ThreatLinePixels;
            var backingWidth = pixelsToWorld * GameVisualTokens.ThreatBackingPixels;
            for (var step = 0; step < steps; step++)
            {
                var from = step / (float)steps; var to = (step + 1f) / steps;
                var opacity = lane.Water ? 1 : (from + to) * .5f;
                var middle = start + delta * ((from + to) * .5f);
                var halfLength = length * (to - from) * .5f;
                var edge = color; edge.a *= opacity;
                var ink = GameVisualTokens.Ink; ink.a *= opacity;
                warningLineBacking.Add(middle + normal * lane.Radius, new Vector2(halfLength, backingWidth * .5f), angle, ink);
                warningLineBacking.Add(middle - normal * lane.Radius, new Vector2(halfLength, backingWidth * .5f), angle, ink);
                warningLines.Add(middle + normal * lane.Radius, new Vector2(halfLength, lineWidth * .5f), angle, edge);
                warningLines.Add(middle - normal * lane.Radius, new Vector2(halfLength, lineWidth * .5f), angle, edge);
                var filledTo = Mathf.Min(to, progress);
                if (filledTo <= from) continue;
                var fill = color; fill.a = opacity * (lane.Active ? GameVisualTokens.ThreatActiveFillOpacity : GameVisualTokens.ThreatTellFillOpacity);
                warningLines.Add(start + delta * ((from + filledTo) * .5f), new Vector2(length * (filledTo - from) * .5f, lane.Radius), angle, fill);
            }
        }

        Rect OverlayWorldBounds()
        {
            var minimum = renderCamera.ScreenToWorldPoint(worldOverlayPixels.min);
            var maximum = renderCamera.ScreenToWorldPoint(worldOverlayPixels.max);
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        void ResetThreatWarnings()
        { explainedCharges.Clear(); warningCaptionUntil = 0; warningLabels?.Clear(); warningSelection.Clear(); }

        void DisposeThreatWarnings()
        { warningLines?.Dispose(); warningLineBacking?.Dispose(); warningArrowBacking?.Dispose(); warningArrows?.Dispose(); warningMeshes?.Dispose(); warningLabels?.Dispose(); }
    }
}
