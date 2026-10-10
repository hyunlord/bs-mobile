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
        ShapeBatch warningLines, warningArrows;
        WorldGrowthLabels warningLabels;
        float warningCaptionUntil;

        void InitializeThreatWarnings()
        {
            if (warningLines == null)
            {
                warningMeshes = new ShapeMeshes();
                var shader = Resources.Load<Shader>("WorldShape");
                warningLines = new ShapeBatch(warningMeshes[WorldShape.Square], shader, renderCamera, GameVisualTokens.WaveDangerLayer);
                warningArrows = new ShapeBatch(warningMeshes[WorldShape.Triangle], shader, renderCamera, GameVisualTokens.WaveDangerLayer + 1);
                warningLabels = new WorldGrowthLabels(transform, "돌진 — 옆으로 피하세요", GameVisualTokens.Hostile);
                warningLabels.Show(0, "돌진 — 옆으로 피하세요", Vector2.zero);
                warningLabels.Clear();
            }
        }

        void DrawThreatWarnings(RunFrame current, float alpha, Vector2 lord)
        {
            warningLines.BeginFrame(); warningArrows.BeginFrame(); warningLabels.BeginFrame(); warningSelection.Clear();
            var cameraPosition = (Vector2)renderCamera.transform.position;
            var halfHeight = renderCamera.orthographicSize; var halfWidth = halfHeight * renderCamera.aspect;
            var visible = new Rect(cameraPosition - new Vector2(halfWidth, halfHeight), new Vector2(halfWidth * 2, halfHeight * 2));
            for (var index = 0; index < wave.Enemies.Count; index++)
            {
                var enemy = wave.Enemies[index];
                if (enemy.Phase != "tell-charge" && enemy.Phase != "tell-water" && enemy.Phase != "water") continue;
                if (!waveActorById.TryGetValue(enemy.Id, out var actor) || actor.Health <= 0) continue;
                var origin = Point(enemy.Origin);
                if (!ThreatCorridors.OriginVisible(visible, origin)) continue;
                var water = enemy.Phase != "tell-charge";
                var lane = new ThreatCorridor { Id = enemy.Id, ImpactTick = enemy.UntilTick,
                    TellTicks = waveDefinition.Enemies[actor.DefinitionId].TellTicks, Origin = origin, Target = Point(enemy.Target),
                    Radius = waveCatalog.Enemies[actor.DefinitionId].Range / (float)settings.WorldUnitsPerUnityUnit,
                    Water = water, Active = enemy.Phase == "water" };
                if (water && !ThreatCorridors.Contains(lane, lord)) continue;
                if ((lane.Target - lane.Origin).sqrMagnitude > .0001f) ThreatCorridors.Consider(lane, warningSelection);
            }
            var pixelsToWorld = halfHeight * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            for (var index = 0; index < warningSelection.Count; index++)
            {
                var lane = warningSelection[index];
                var vector = lane.Target - lane.Origin; var length = vector.magnitude; var axis = vector / length;
                ThreatCorridors.CoveredSegment(lane, out var coveredStart, out var coveredEnd);
                var coveredVector = coveredEnd - coveredStart; var coveredLength = coveredVector.magnitude;
                var normal = new Vector2(-axis.y, axis.x); var angle = Angle(axis); var middle = (coveredStart + coveredEnd) * .5f;
                var color = lane.Water ? Color.Lerp(GameVisualTokens.Hostile, GameVisualTokens.Ally, .25f) : GameVisualTokens.Hostile;
                var lineWidth = pixelsToWorld * 1.5f;
                warningLines.Add(middle + normal * lane.Radius, new Vector2(coveredLength * .5f, lineWidth * .5f), angle, color);
                warningLines.Add(middle - normal * lane.Radius, new Vector2(coveredLength * .5f, lineWidth * .5f), angle, color);
                warningLines.Add(coveredStart, new Vector2(lineWidth * .5f, lane.Radius), angle, color);
                warningLines.Add(coveredEnd, new Vector2(lineWidth * .5f, lane.Radius), angle, color);
                warningLines.Add(lane.Origin, new Vector2(lineWidth, Mathf.Min(lane.Radius, pixelsToWorld * 5)), angle, color);
                var progress = ThreatCorridors.Progress(lane, current.Tick, alpha);
                var fill = color; fill.a = lane.Active ? .2f : .1f;
                warningLines.Add(coveredStart + coveredVector * progress * .5f, new Vector2(coveredLength * progress * .5f, lane.Radius), angle, fill);
                var arrowLength = Mathf.Min(length * .16f, pixelsToWorld * 11);
                var arrowWidth = Mathf.Min(lane.Radius * .6f, pixelsToWorld * 6);
                warningArrows.Add(lane.Origin + vector * .7f, new Vector2(arrowLength, arrowWidth), angle, color);
                if (!lane.Water && explainedCharges.Count < 2 && explainedCharges.Add((lane.Id, lane.ImpactTick))) warningCaptionUntil = visualTime + 2;
            }
            if (visualTime < warningCaptionUntil)
                warningLabels.Show(0, "돌진 — 옆으로 피하세요", lord + Vector2.up * .75f);
            warningLines.Flush(); warningArrows.Flush();
        }

        void ResetThreatWarnings()
        { explainedCharges.Clear(); warningCaptionUntil = 0; warningLabels?.Clear(); warningSelection.Clear(); }

        void DisposeThreatWarnings()
        { warningLines?.Dispose(); warningArrows?.Dispose(); warningMeshes?.Dispose(); warningLabels?.Dispose(); }
    }
}
