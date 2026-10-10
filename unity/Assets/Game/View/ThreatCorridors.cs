using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    public struct ThreatCorridor
    {
        public int Id, ImpactTick, TellTicks;
        public Vector2 Origin, Target;
        public float Radius;
        public bool Water, Active;
    }

    public static class ThreatCorridors
    {
        public const int VisibleLimit = 3;
        static int Earliest(ThreatCorridor a, ThreatCorridor b)
        {
            var order = a.ImpactTick.CompareTo(b.ImpactTick);
            return order != 0 ? order : a.Id.CompareTo(b.Id);
        }

        public static bool OriginVisible(Rect viewport, Vector2 origin) => viewport.Contains(origin);

        public static bool Contains(ThreatCorridor lane, Vector2 point)
        {
            var direction = lane.Target - lane.Origin;
            var progress = direction.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point - lane.Origin, direction) / direction.sqrMagnitude) : 0;
            return (point - lane.Origin - direction * progress).sqrMagnitude <= lane.Radius * lane.Radius;
        }

        public static float Progress(ThreatCorridor lane, int tick, float alpha)
            => lane.Active ? 1 : Mathf.Clamp01(1 - (lane.ImpactTick - tick + 1 - Mathf.Clamp01(alpha)) / Mathf.Max(1, lane.TellTicks));

        public static void CoveredSegment(ThreatCorridor lane, out Vector2 start, out Vector2 end)
        {
            var axis = (lane.Target - lane.Origin).normalized;
            start = lane.Origin - axis * lane.Radius; end = lane.Target + axis * lane.Radius;
        }

        public static void Select(List<ThreatCorridor> candidates, List<ThreatCorridor> selected)
        {
            selected.Clear();
            for (var i = 0; i < candidates.Count; i++)
                Consider(candidates[i], selected);
        }

        public static void Consider(ThreatCorridor candidate, List<ThreatCorridor> selected)
        {
            for (var index = 0; index < selected.Count; index++)
            {
                var prior = selected[index];
                var earliest = Earliest(prior, candidate) <= 0 ? prior : candidate;
                var later = Earliest(prior, candidate) <= 0 ? candidate : prior;
                if (TryMerge(ref earliest, later))
                {
                    candidate = earliest;
                    selected.RemoveAt(index);
                    index = -1;
                }
            }
            if (selected.Count < VisibleLimit) selected.Add(candidate);
            else if (Earliest(candidate, selected[selected.Count - 1]) < 0) selected[selected.Count - 1] = candidate;
            else return;
            SortSelected(selected);
        }

        static void SortSelected(List<ThreatCorridor> selected)
        {
            for (var i = 1; i < selected.Count; i++)
                for (var j = i; j > 0 && Earliest(selected[j], selected[j - 1]) < 0; j--)
                { var swap = selected[j]; selected[j] = selected[j - 1]; selected[j - 1] = swap; }
        }

        static bool TryMerge(ref ThreatCorridor prior, ThreatCorridor candidate)
        {
            if (prior.Water != candidate.Water || prior.Active != candidate.Active) return false;
            var axis = (prior.Target - prior.Origin).normalized;
            var nextAxis = (candidate.Target - candidate.Origin).normalized;
            if (Vector2.Dot(axis, nextAxis) < .985f) return false;
            var normal = new Vector2(-axis.y, axis.x);
            var a = candidate.Origin - prior.Origin; var b = candidate.Target - prior.Origin;
            var lateral = Mathf.Max(Mathf.Abs(Vector2.Dot(a, normal)), Mathf.Abs(Vector2.Dot(b, normal)));
            var from = Vector2.Dot(a, axis); var to = Vector2.Dot(b, axis);
            var length = Vector2.Distance(prior.Origin, prior.Target);
            if (lateral > prior.Radius + candidate.Radius || from > length || to < 0) return false;
            // Conservatively cover the union while retaining the earliest impact and its direction.
            var origin = prior.Origin;
            prior.Origin = origin + axis * Mathf.Min(0, from);
            prior.Target = origin + axis * Mathf.Max(length, to);
            prior.Radius = Mathf.Max(prior.Radius, lateral + candidate.Radius);
            return true;
        }
    }
}
