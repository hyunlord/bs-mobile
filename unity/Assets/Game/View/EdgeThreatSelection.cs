using UnityEngine;

namespace Game.View
{
    public enum EdgeThreatKind { Ordinary, Charger, Elite, Boss }

    public readonly struct EdgeThreat
    {
        public readonly int Id;
        public readonly Vector2 Position, Direction;
        public readonly float Distance, Opacity;
        public readonly EdgeThreatKind Kind;
        public readonly bool InboundCharge;
        public EdgeThreat(int id, Vector2 position, Vector2 direction, float distance, float opacity, EdgeThreatKind kind, bool inboundCharge)
        { Id = id; Position = position; Direction = direction; Distance = distance; Opacity = opacity; Kind = kind; InboundCharge = inboundCharge; }
    }

    public sealed class EdgeThreatSelection
    {
        public const int Limit = 6;
        readonly EdgeThreat[] slots = new EdgeThreat[Limit];
        readonly bool[] occupied = new bool[Limit];
        Rect visible, inset;
        Vector2 lord;
        float fadeDistance;
        public void Begin(Rect viewport, Vector2 lordPosition, float padding)
        {
            visible = viewport; lord = lordPosition;
            padding = Mathf.Min(Mathf.Max(0, padding), Mathf.Min(viewport.width, viewport.height) * .25f);
            inset = Rect.MinMaxRect(viewport.xMin + padding, viewport.yMin + padding, viewport.xMax - padding, viewport.yMax - padding);
            fadeDistance = Mathf.Max(.001f, viewport.height);
            for (var i = 0; i < Limit; i++) occupied[i] = false;
        }
        public bool TryGet(int sector, out EdgeThreat threat)
        { threat = slots[sector]; return occupied[sector]; }
        public void Consider(int id, Vector2 position, EdgeThreatKind kind, bool inboundCharge)
        {
            if (visible.Contains(position) || inset.width <= 0 || inset.height <= 0) return;
            var delta = position - inset.center;
            if (delta.sqrMagnitude < .000001f) return;
            var direction = delta.normalized;
            var sector = Mathf.FloorToInt((Mathf.Atan2(direction.y, direction.x) + Mathf.PI) / (2 * Mathf.PI) * Limit) % Limit;
            var distance = (position - lord).sqrMagnitude;
            var prior = slots[sector];
            var priority = Priority(kind, inboundCharge);
            if (occupied[sector])
            {
                var priorPriority = Priority(prior.Kind, prior.InboundCharge);
                if (priority < priorPriority || priority == priorPriority && (distance > prior.Distance || distance == prior.Distance && id >= prior.Id)) return;
            }
            var edge = Mathf.Min(Mathf.Abs(direction.x) > .00001f ? inset.width * .5f / Mathf.Abs(direction.x) : float.PositiveInfinity,
                Mathf.Abs(direction.y) > .00001f ? inset.height * .5f / Mathf.Abs(direction.y) : float.PositiveInfinity);
            var outside = position - new Vector2(Mathf.Clamp(position.x, visible.xMin, visible.xMax), Mathf.Clamp(position.y, visible.yMin, visible.yMax));
            var opacity = Mathf.Lerp(.9f, .22f, Mathf.Clamp01(outside.magnitude / fadeDistance));
            slots[sector] = new EdgeThreat(id, inset.center + direction * edge, direction, distance, opacity, kind, inboundCharge);
            occupied[sector] = true;
        }
        static int Priority(EdgeThreatKind kind, bool inboundCharge) => inboundCharge ? 4 : kind == EdgeThreatKind.Boss ? 3 : kind == EdgeThreatKind.Elite ? 2 : kind == EdgeThreatKind.Charger ? 1 : 0;
        public static bool IsInboundCharge(string phase, bool stopped, int untilTick, int tick, int tickRate, Rect viewport, Vector2 origin, Vector2 target, float radius)
        {
            if (stopped || phase != "tell-charge" || tickRate <= 0 || viewport.Contains(origin)) return false;
            var remaining = untilTick - tick;
            return remaining > 0 && remaining <= tickRate * .5f && ThreatCorridors.Intersects(viewport,
                new ThreatCorridor { Origin = origin, Target = target, Radius = radius });
        }
        public static Color Tint(EdgeThreatKind kind) => kind == EdgeThreatKind.Boss ? new Color32(0xD7, 0x6B, 0xD0, 255)
            : kind == EdgeThreatKind.Elite ? new Color32(0xE5, 0xB8, 0x59, 255)
            : kind == EdgeThreatKind.Charger ? GameVisualTokens.Hostile : new Color32(0xBD, 0xB6, 0x9B, 255);
    }
}
