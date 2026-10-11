using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    public struct ThreatCorridor
    {
        public int Id, ImpactTick, TellTicks;
        public Vector2 Origin, Target;
        public float Radius;
        public float SourceRadius, SourceLength;
        public bool Water, Active;
    }

    public static class ThreatCorridors
    {
        public const int VisibleLimit = 3;
        public const float MaximumWidthMultiplier = 1.5f, MaximumLengthMultiplier = 1.25f;
        const float MinimumDirectionDot = .99939f, MinimumSharedLengthFraction = .8f;
        static int Earliest(ThreatCorridor a, ThreatCorridor b)
        {
            var order = a.ImpactTick.CompareTo(b.ImpactTick);
            return order != 0 ? order : a.Id.CompareTo(b.Id);
        }

        public static bool OriginVisible(Rect viewport, Vector2 origin) => viewport.Contains(origin);

        public static bool ClipSegment(Rect viewport, ref Vector2 start, ref Vector2 end)
        {
            var delta = end - start;
            var entry = 0f; var exit = 1f;
            if (!ClipAxis(start.x, delta.x, viewport.xMin, viewport.xMax, ref entry, ref exit) ||
                !ClipAxis(start.y, delta.y, viewport.yMin, viewport.yMax, ref entry, ref exit)) return false;
            end = start + delta * exit; start += delta * entry;
            return true;
        }

        static bool ClipAxis(float origin, float delta, float minimum, float maximum, ref float entry, ref float exit)
        {
            if (Mathf.Abs(delta) < .000001f) return origin >= minimum && origin <= maximum;
            var a = (minimum - origin) / delta; var b = (maximum - origin) / delta;
            entry = Mathf.Max(entry, Mathf.Min(a, b)); exit = Mathf.Min(exit, Mathf.Max(a, b));
            return entry <= exit;
        }

        public static bool Intersects(Rect viewport, ThreatCorridor lane)
        {
            var start = lane.Origin; var end = lane.Target;
            if (ClipSegment(viewport, ref start, ref end)) return true;
            if (Contains(lane, viewport.min) || Contains(lane, viewport.max) ||
                Contains(lane, new Vector2(viewport.xMin, viewport.yMax)) || Contains(lane, new Vector2(viewport.xMax, viewport.yMin))) return true;
            return DistanceToRect(viewport, lane.Origin) <= lane.Radius * lane.Radius || DistanceToRect(viewport, lane.Target) <= lane.Radius * lane.Radius;
        }

        static float DistanceToRect(Rect rect, Vector2 point)
            => (point - new Vector2(Mathf.Clamp(point.x, rect.xMin, rect.xMax), Mathf.Clamp(point.y, rect.yMin, rect.yMax))).sqrMagnitude;

        public static Vector2 VisibleArrowPosition(Rect viewport, ThreatCorridor lane, float inset)
        {
            CoveredSegment(lane, out var start, out var end);
            if (ClipSegment(viewport, ref start, ref end)) return Vector2.Lerp(start, end, .7f);
            // The capsule can graze the viewport while its entire centerline remains outside.
            var preferred = Vector2.Lerp(lane.Origin, lane.Target, .7f);
            var point = new Vector2(Mathf.Clamp(preferred.x, viewport.xMin, viewport.xMax), Mathf.Clamp(preferred.y, viewport.yMin, viewport.yMax));
            var distance = DistanceToLane(lane, point);
            ConsiderArrowPoint(lane, viewport.min, ref point, ref distance);
            ConsiderArrowPoint(lane, viewport.max, ref point, ref distance);
            ConsiderArrowPoint(lane, new Vector2(viewport.xMin, viewport.yMax), ref point, ref distance);
            ConsiderArrowPoint(lane, new Vector2(viewport.xMax, viewport.yMin), ref point, ref distance);
            var origin = lane.Origin; var target = lane.Target;
            ConsiderArrowPoint(lane, new Vector2(Mathf.Clamp(origin.x, viewport.xMin, viewport.xMax), Mathf.Clamp(origin.y, viewport.yMin, viewport.yMax)), ref point, ref distance);
            ConsiderArrowPoint(lane, new Vector2(Mathf.Clamp(target.x, viewport.xMin, viewport.xMax), Mathf.Clamp(target.y, viewport.yMin, viewport.yMax)), ref point, ref distance);
            var slack = Mathf.Max(0, lane.Radius - Mathf.Sqrt(distance));
            return point + (viewport.center - point).normalized * Mathf.Min(inset, slack * .5f);
        }

        static float DistanceToLane(ThreatCorridor lane, Vector2 point)
        {
            var delta = lane.Target - lane.Origin;
            var t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point - lane.Origin, delta) / delta.sqrMagnitude) : 0;
            return (point - lane.Origin - delta * t).sqrMagnitude;
        }

        static void ConsiderArrowPoint(ThreatCorridor lane, Vector2 candidate, ref Vector2 point, ref float distance)
        {
            var next = DistanceToLane(lane, candidate);
            if (next < distance) { distance = next; point = candidate; }
        }

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

        public static void LocalSegment(ThreatCorridor lane, Vector2 lord, float maximumLength, out Vector2 start, out Vector2 end)
        {
            CoveredSegment(lane, out start, out end);
            if (lane.Water) return;
            var axis = (end - start).normalized;
            var length = Vector2.Distance(start, end);
            var limit = Mathf.Max(0, maximumLength);
            var terminal = Mathf.Clamp(Vector2.Dot(lord - start, axis) + lane.Radius, 0, length);
            var from = Mathf.Max(0, terminal - limit);
            end = start + axis * terminal;
            start += axis * from;
        }

        public static Vector2 EntryArrowPosition(Rect viewport, ThreatCorridor lane, float inset)
        {
            var start = lane.Origin; var end = lane.Target;
            if (ClipSegment(viewport, ref start, ref end))
                return Vector2.MoveTowards(start, end, Mathf.Min(inset, Vector2.Distance(start, end) * .5f));
            var point = VisibleArrowPosition(viewport, lane, 0);
            var axis = (lane.Target - lane.Origin).normalized;
            ConsiderEntryPoint(lane, axis, viewport.min, ref point);
            ConsiderEntryPoint(lane, axis, viewport.max, ref point);
            ConsiderEntryPoint(lane, axis, new Vector2(viewport.xMin, viewport.yMax), ref point);
            ConsiderEntryPoint(lane, axis, new Vector2(viewport.xMax, viewport.yMin), ref point);
            var closestOrigin = new Vector2(Mathf.Clamp(lane.Origin.x, viewport.xMin, viewport.xMax), Mathf.Clamp(lane.Origin.y, viewport.yMin, viewport.yMax));
            ConsiderEntryPoint(lane, axis, closestOrigin, ref point);
            var slack = Mathf.Max(0, lane.Radius - Mathf.Sqrt(DistanceToLane(lane, point)));
            return point + (viewport.center - point).normalized * Mathf.Min(inset, slack * .5f);
        }

        static void ConsiderEntryPoint(ThreatCorridor lane, Vector2 axis, Vector2 candidate, ref Vector2 point)
        {
            if (Contains(lane, candidate) && Vector2.Dot(candidate - point, axis) < 0) point = candidate;
        }

        public static void Select(List<ThreatCorridor> candidates, List<ThreatCorridor> selected)
        {
            selected.Clear();
            for (var i = 0; i < candidates.Count; i++)
                Consider(candidates[i], selected);
        }

        public static void Consider(ThreatCorridor candidate, List<ThreatCorridor> selected)
        {
            if (candidate.SourceRadius <= 0) candidate.SourceRadius = candidate.Radius;
            if (candidate.SourceLength <= 0) candidate.SourceLength = Vector2.Distance(candidate.Origin, candidate.Target);
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
            if (Vector2.Dot(axis, nextAxis) < MinimumDirectionDot) return false;
            var normal = new Vector2(-axis.y, axis.x);
            var a = candidate.Origin - prior.Origin; var b = candidate.Target - prior.Origin;
            var lateralA = Vector2.Dot(a, normal); var lateralB = Vector2.Dot(b, normal);
            var from = Vector2.Dot(a, axis); var to = Vector2.Dot(b, axis);
            var length = Vector2.Distance(prior.Origin, prior.Target);
            var overlap = Mathf.Min(length, to) - Mathf.Max(0, from);
            if (overlap < Mathf.Min(length, to - from) * MinimumSharedLengthFraction) return false;
            var lower = Mathf.Min(-prior.Radius, Mathf.Min(lateralA, lateralB) - candidate.Radius);
            var upper = Mathf.Max(prior.Radius, Mathf.Max(lateralA, lateralB) + candidate.Radius);
            var radius = (upper - lower) * .5f;
            var sourceRadius = Mathf.Max(prior.SourceRadius, candidate.SourceRadius);
            var sourceLength = Mathf.Max(prior.SourceLength, candidate.SourceLength);
            var unionStart = Mathf.Min(0, from); var unionEnd = Mathf.Max(length, to);
            // Caps refer to original inputs, never the enlarged union, so chains cannot widen their own eligibility.
            if (radius > sourceRadius * MaximumWidthMultiplier || unionEnd - unionStart > sourceLength * MaximumLengthMultiplier) return false;
            var origin = prior.Origin;
            var center = normal * ((lower + upper) * .5f);
            prior.Origin = origin + axis * unionStart + center;
            prior.Target = origin + axis * unionEnd + center;
            prior.Radius = radius; prior.SourceRadius = sourceRadius; prior.SourceLength = sourceLength;
            return true;
        }
    }
}
