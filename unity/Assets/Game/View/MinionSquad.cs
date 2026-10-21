using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    public enum MinionState { Spawn, Seek, Attack, Return, Despawn }
    public enum MinionMode { Seek, Return }

    // "소환 부하 행동" spec. Frames are 60Hz display frames; distances are in hero body widths.
    public static class MinionSpec
    {
        public const int SpawnFrames = 5, WindupFrames = 3, ThrustFrames = 2, RecoverFrames = 4;
        public const float DespawnSeconds = .3f, WindupTimeoutSeconds = .15f;
        public const float RunSpeed = 9, ChargeSpeed = 13, Steering = 12;            // body widths per second, 1/s
        public const float Separation = .75f, Reach = .6f, Leash = 3.2f, HeroRing = 1.9f, StationaryStep = .015f;
        public const int MaxThrustersPerHit = 2;
        public static float Seconds(int frames) => frames / 60f;
        public static float AttackSeconds => Seconds(WindupFrames + ThrustFrames + RecoverFrames);
    }

    public struct MinionAgent
    {
        public int Group, Index;
        public MinionState State;
        public Vector2 Position, Previous, Facing, AttackTarget;
        public float StateStarted, AttackStarted, Stride, Scale;
        public bool HitArrived, Charge;
        public float AttackAge(float now) => now - AttackStarted;
        // 0 wind-up, 1 thrust, 2 recover, -1 not attacking.
        public int AttackPhase(float now)
        {
            if (State != MinionState.Attack) return -1;
            var age = now - AttackStarted;
            if (age < MinionSpec.Seconds(MinionSpec.WindupFrames)) return 0;
            if (age < MinionSpec.Seconds(MinionSpec.WindupFrames + MinionSpec.ThrustFrames)) return 1;
            return 2;
        }
    }

    // View-only muster soldiers. Core owns the group's position, phase and hits; each soldier is a small body that runs to the
    // group's current target within a leash of the Core group, thrusts on the Core hit frame, returns beside the hero and
    // disperses when the group ends. Deterministic for a given input sequence; no per-frame allocation after warm-up.
    public sealed class MinionSquad
    {
        public const int Capacity = 64;
        public struct GroupInput
        {
            public int Id, Count;
            public MinionMode Mode;
            public Vector2 Anchor, Target, Hero;
            public bool HasTarget, Charge;
            public float Scale;
            public float NextHitIn;      // seconds until the next predicted Core hit, negative when unknown
        }
        readonly MinionAgent[] agents = new MinionAgent[Capacity];
        readonly Dictionary<int, GroupInput> groups = new Dictionary<int, GroupInput>(16);
        readonly HashSet<int> synced = new HashSet<int>();
        readonly List<int> stale = new List<int>(16);
        public int Count { get; private set; }
        public ref readonly MinionAgent this[int index] => ref agents[index];
        public long AgentFrames { get; private set; }
        public long StationaryFrames { get; private set; }
        public int GroupHits { get; private set; }
        public int ThrustsOnHitFrame { get; private set; }
        public int Dropped { get; private set; }

        public void Clear() { Count = 0; groups.Clear(); synced.Clear(); AgentFrames = StationaryFrames = 0; GroupHits = ThrustsOnHitFrame = Dropped = 0; }
        public void BeginSync() => synced.Clear();
        public bool TryGetGroup(int id, out GroupInput group) => groups.TryGetValue(id, out group);

        public void SyncGroup(in GroupInput group, float now)
        {
            groups[group.Id] = group; synced.Add(group.Id);
            var alive = 0;
            for (var i = 0; i < Count; i++)
            {
                if (agents[i].Group != group.Id || agents[i].State == MinionState.Despawn) continue;
                agents[i].Scale = group.Scale; agents[i].Charge = group.Charge;
                if (agents[i].Index >= group.Count) Retire(ref agents[i], now); else alive |= 1 << Mathf.Min(30, agents[i].Index);
            }
            for (var index = 0; index < group.Count && index < 31; index++)
            {
                if ((alive & (1 << index)) != 0) continue;
                if (Count == Capacity) { Dropped++; continue; }
                var start = group.Hero + Ring(index, group.Count) * .25f;
                agents[Count++] = new MinionAgent { Group = group.Id, Index = index, State = MinionState.Spawn, Position = start, Previous = start,
                    Facing = Vector2.right, StateStarted = now, Scale = group.Scale, Charge = group.Charge };
            }
        }

        // Groups not synced this frame have ended in Core: their soldiers disperse.
        public void EndSync(float now)
        {
            stale.Clear();
            foreach (var id in groups.Keys) if (!synced.Contains(id)) stale.Add(id);
            for (var s = 0; s < stale.Count; s++)
            {
                groups.Remove(stale[s]);
                for (var i = 0; i < Count; i++) if (agents[i].Group == stale[s] && agents[i].State != MinionState.Despawn) Retire(ref agents[i], now);
            }
        }

        static void Retire(ref MinionAgent agent, float now) { agent.State = MinionState.Despawn; agent.StateStarted = now; }

        // A Core group hit: the soldiers nearest the struck enemy thrust on this very frame (wind-up already shown, or skipped).
        public int GroupHit(int group, Vector2 at, float now)
        {
            GroupHits++;
            var started = 0;
            for (var pick = 0; pick < MinionSpec.MaxThrustersPerHit; pick++)
            {
                var best = -1; var bestDistance = float.PositiveInfinity;
                for (var i = 0; i < Count; i++)
                {
                    ref var a = ref agents[i];
                    if (a.Group != group || a.State == MinionState.Despawn) continue;
                    if (a.State == MinionState.Attack && a.HitArrived) continue;
                    var distance = (a.Position - at).sqrMagnitude;
                    if (distance < bestDistance) { bestDistance = distance; best = i; }
                }
                if (best < 0) break;
                ref var chosen = ref agents[best];
                chosen.State = MinionState.Attack; chosen.HitArrived = true; chosen.AttackTarget = at;
                chosen.AttackStarted = now - MinionSpec.Seconds(MinionSpec.WindupFrames);
                started++;
            }
            if (started > 0) ThrustsOnHitFrame++;
            return started;
        }

        public static Vector2 Ring(int index, int count)
        {
            var angle = (index + .5f) / Mathf.Max(1, count) * Mathf.PI * 2 + .6f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        public void Step(float now, float dt, float bodyWidth)
        {
            dt = Mathf.Max(0, dt);
            var w = Mathf.Max(.0001f, bodyWidth);
            for (var i = Count - 1; i >= 0; i--)
            {
                ref var a = ref agents[i];
                a.Previous = a.Position;
                if (a.State == MinionState.Despawn && now - a.StateStarted >= MinionSpec.DespawnSeconds) { agents[i] = agents[--Count]; continue; }
                if (!groups.TryGetValue(a.Group, out var g)) continue;
                if (a.State == MinionState.Spawn && now - a.StateStarted >= MinionSpec.Seconds(MinionSpec.SpawnFrames)) { a.State = g.Mode == MinionMode.Seek ? MinionState.Seek : MinionState.Return; a.StateStarted = now; }
                if (a.State == MinionState.Seek && g.Mode == MinionMode.Return) { a.State = MinionState.Return; a.StateStarted = now; }
                if (a.State == MinionState.Return && g.Mode == MinionMode.Seek) { a.State = MinionState.Seek; a.StateStarted = now; }
                if (a.State == MinionState.Attack)
                {
                    var age = now - a.AttackStarted;
                    var thrustAt = MinionSpec.Seconds(MinionSpec.WindupFrames);
                    // A predicted wind-up holds at full draw until Core's hit arrives, then gives up quietly.
                    if (!a.HitArrived)
                    {
                        if (now - a.StateStarted >= thrustAt + MinionSpec.WindupTimeoutSeconds) { a.State = MinionState.Seek; a.StateStarted = now; }
                        else if (age >= thrustAt) a.AttackStarted = now - thrustAt + .0001f;
                    }
                    else if (age >= MinionSpec.AttackSeconds) { a.State = g.Mode == MinionMode.Seek ? MinionState.Seek : MinionState.Return; a.StateStarted = now; a.HitArrived = false; }
                }
                // Predictive wind-up so the thrust can land on the Core hit frame.
                if (a.State == MinionState.Seek && g.HasTarget && g.NextHitIn >= 0 && g.NextHitIn <= MinionSpec.Seconds(MinionSpec.WindupFrames) &&
                    (a.Position - g.Target).magnitude <= (MinionSpec.Reach + 1.2f) * w)
                { a.State = MinionState.Attack; a.HitArrived = false; a.AttackStarted = a.StateStarted = now; a.AttackTarget = g.Target; }

                var desired = Desired(a, g, w, now);
                var speed = (g.Charge ? MinionSpec.ChargeSpeed : MinionSpec.RunSpeed) * w;
                var velocity = Vector2.ClampMagnitude((desired - a.Position) * MinionSpec.Steering, speed);
                // Separation: push apart from every other soldier closer than the separation radius.
                for (var j = 0; j < Count; j++)
                {
                    if (j == i || agents[j].State == MinionState.Despawn) continue;
                    var away = a.Position - agents[j].Position; var distance = away.magnitude;
                    var radius = MinionSpec.Separation * w;
                    if (distance >= radius) continue;
                    velocity += (distance > .00001f ? away / distance : Ring(a.Index, 7)) * (radius - distance) * MinionSpec.Steering * 1.5f;
                }
                if (a.State == MinionState.Attack || a.State == MinionState.Spawn) velocity *= .35f;
                a.Position += velocity * dt;
                var moved = (a.Position - a.Previous).magnitude;
                a.Stride += moved / w;
                var heading = a.State == MinionState.Attack ? a.AttackTarget - a.Position : velocity;
                if (heading.sqrMagnitude > .000001f) a.Facing = heading.normalized;
                AgentFrames++;
                var animating = a.State == MinionState.Spawn || a.State == MinionState.Attack || a.State == MinionState.Despawn;
                if (!animating && moved < MinionSpec.StationaryStep * w) StationaryFrames++;
            }
        }

        Vector2 Desired(in MinionAgent a, in GroupInput g, float w, float now)
        {
            if (a.State == MinionState.Despawn) return a.Position;
            if (a.State == MinionState.Return || a.State == MinionState.Spawn && g.Mode == MinionMode.Return || !g.HasTarget)
            {
                // Beside the hero: a slowly turning ring so waiting soldiers still shuffle instead of freezing.
                var turn = now * .9f + a.Index * .37f;
                var offset = Ring(a.Index, g.Count);
                offset = new Vector2(offset.x * Mathf.Cos(turn * .2f) - offset.y * Mathf.Sin(turn * .2f), offset.x * Mathf.Sin(turn * .2f) + offset.y * Mathf.Cos(turn * .2f));
                return g.Hero + offset * MinionSpec.HeroRing * w;
            }
            if (g.Charge)
            {
                // Evolution charge: a wedge behind the flag bearer (index 0) driving at the target.
                var forward = (g.Target - g.Anchor).sqrMagnitude > .000001f ? (g.Target - g.Anchor).normalized : Vector2.right;
                var side = new Vector2(-forward.y, forward.x);
                var rank = (a.Index + 1) / 2; var lane = a.Index == 0 ? 0 : (a.Index & 1) == 0 ? 1 : -1;
                var tip = g.Target - forward * MinionSpec.Reach * w;
                return ClampToLeash(tip - forward * rank * .8f * w + side * lane * rank * .7f * w, g.Anchor, w);
            }
            var slot = g.Target + Ring(a.Index, g.Count) * MinionSpec.Reach * w;
            // Soldiers circle their target a little while waiting for the next hit so they keep moving.
            var orbit = new Vector2(Mathf.Sin(now * 3.1f + a.Index), Mathf.Cos(now * 2.7f + a.Index * 1.3f)) * .18f * w;
            return ClampToLeash(slot + orbit, g.Anchor, w);
        }

        static Vector2 ClampToLeash(Vector2 point, Vector2 anchor, float w)
        {
            var offset = point - anchor; var leash = MinionSpec.Leash * w;
            return offset.magnitude <= leash ? point : anchor + offset.normalized * leash;
        }
    }
}
