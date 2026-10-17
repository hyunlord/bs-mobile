using System;
using System.Collections.Generic;
namespace SowSiege.Core
{
    internal sealed class WavePendingImpact
    {
        public int Id, Tick, Radius, Damage, Knockback, ActivationId;
        public string Source = "";
        public Position Position;
        // Ricochet arrows (ADR0062) follow TargetId while it lives, may bounce BouncesLeft more times and never strike an
        // enemy in Struck again (other arrows may); Struck is null for every other placement.
        public int TargetId, BouncesLeft;
        public List<int>? Struck;
        public Position From;
    }
    internal sealed partial class WaveRuntimeSystem
    {
        private void ScheduleReviewImpacts(WaveGearDefinition gear)
        {
            var c = gear.Composition!;
            var ricochet = c.Placement == "ricochet";
            if (c.Version != 1 || c.Placement is not ("nearest-target" or "forward-line" or "ricochet") || c.DelayTicks < 1 || c.StepTicks < 0 || c.Spacing < 0 || c.Radius < 1 || gear.Count < 1 || gear.Count > WaveAttackComposition.MaximumImpacts ||
                ricochet != (c.Bounces is not null) || (ricochet && (c.Bounces is not (>= 0 and <= WaveAttackComposition.MaximumImpacts) || c.StepTicks < 1 || c.RehitEachImpact is not null)))
            { throw new ArgumentException("Unsupported review impact composition."); }
            if (ricochet) { ScheduleRicochet(gear, c); return; }
            queries.ByDistance(targets, world.Lord, gear.Range, world.Lord);
            var count = c.Placement == "nearest-target" ? Math.Min(gear.Count, targets.Count) : gear.Count;
            var activation = state.BeginActivation(gear.Id, count, world.Tick + c.DelayTicks + c.StepTicks * count + 1);
            if (count == 0) { state.ResolveActivation(activation); return; }
            var direction = state.Facing;
            if (targets.Count > 0 && targets[0].Position != world.Lord)

            {
                direction = new(targets[0].Position.X - world.Lord.X, targets[0].Position.Y - world.Lord.Y);
            }

            for (var i = 0; i < count; i++)
            {
                var point = c.Placement == "nearest-target" ? targets[i].Position
                    : world.Lord.MoveToward(new(world.Lord.X + direction.X * gear.Range, world.Lord.Y + direction.Y * gear.Range), c.Spacing * (i + 1));
                var pending = new WavePendingImpact
                {
                    Id = world.AllocateId(),
                    Source = gear.Id,
                    Position = point,
                    Tick = world.Tick + c.DelayTicks + i * c.StepTicks,
                    Radius = c.Radius,
                    Damage = gear.Damage,
                    Knockback = gear.Knockback,
                    ActivationId = activation
                };
                state.PendingImpacts.Add(pending);
                state.Emit(world.Tick, "review-impact-tell", gear.Id, pending.Id, point, point, c.Radius);
            }
        }
        private void ScheduleRicochet(WaveGearDefinition gear, WaveAttackComposition c)
        {
            queries.ByDistance(targets, world.Lord, gear.Range, world.Lord);
            var count = Math.Min(gear.Count, targets.Count);
            var activation = state.BeginActivation(gear.Id, count, world.Tick + c.DelayTicks + c.StepTicks * c.Bounces!.Value + 1);
            if (count == 0) { state.ResolveActivation(activation); return; }
            for (var i = 0; i < count; i++)
            {
                QueueArrow(gear, c.Radius, activation, targets[i], c.Bounces.Value, new(), world.Tick + c.DelayTicks, world.Lord);
            }
        }
        private void QueueArrow(WaveGearDefinition gear, int radius, int activation, EnemyState target, int bounces, List<int> struck, int tick, Position from)
        {
            var pending = new WavePendingImpact
            {
                Id = world.AllocateId(),
                Source = gear.Id,
                Position = target.Position,
                Tick = tick,
                Radius = radius,
                Damage = gear.Damage,
                Knockback = gear.Knockback,
                ActivationId = activation,
                TargetId = target.Id,
                BouncesLeft = bounces,
                Struck = struck,
                From = from
            };
            state.PendingImpacts.Add(pending);
            state.Emit(world.Tick, "review-impact-tell", gear.Id, pending.Id, from, target.Position, radius);
        }
        // An arrow strikes its followed target (and anything within its half width it has not struck), pushing them along its
        // flight, then turns to the nearest live enemy within range it has not struck yet.
        private void ResolveRicochet(WavePendingImpact impact, List<int> struck)
        {
            queries.ByDistance(secondaryTargets, impact.Position, impact.Radius, impact.Position);
            foreach (var enemy in secondaryTargets)
            {
                if (enemy.Health > 0 && !struck.Contains(enemy.Id)) { struck.Add(enemy.Id); Hit(enemy, impact.Damage, impact.Source, impact.From, impact.Knockback, impact.ActivationId); }
            }
            state.Emit(world.Tick, "review-impact", impact.Source, impact.Id, impact.Position, impact.Position, impact.Radius);
            var gear = definition.Gear[impact.Source];
            if (impact.BouncesLeft > 0)
            {
                queries.ByDistance(secondaryTargets, impact.Position, gear.Range, impact.Position);
                foreach (var enemy in secondaryTargets)
                {
                    if (enemy.Health <= 0 || struck.Contains(enemy.Id)) { continue; }
                    state.AddChild(impact.ActivationId);
                    QueueArrow(gear, impact.Radius, impact.ActivationId, enemy, impact.BouncesLeft - 1, struck, world.Tick + gear.Composition!.StepTicks, impact.Position);
                    break;
                }
            }
            state.ResolveChild(impact.ActivationId);
        }
        private void TickReviewImpacts()
        {
            for (var i = 0; i < state.PendingImpacts.Count;)
            {
                var impact = state.PendingImpacts[i];
                if (impact.Struck is not null && queries.Find(impact.TargetId) is { } followed) { impact.Position = followed.Position; }
                if (impact.Tick > world.Tick) { i++; continue; }
                if (impact.Struck is { } struck) { state.PendingImpacts.RemoveAt(i); ResolveRicochet(impact, struck); continue; }
                queries.ByDistance(secondaryTargets, impact.Position, impact.Radius, impact.Position);
                // ADR0061: with RehitEachImpact each impact strikes everything in its radius (one query lists an enemy once).
                var rehit = definition.Gear.TryGetValue(impact.Source, out var source) && source.Composition?.RehitEachImpact == true;
                foreach (var enemy in secondaryTargets)
                {
                    if (state.Activations[impact.ActivationId].HitTargets.Add(enemy.Id) || rehit)
                    {
                        Hit(enemy, impact.Damage, impact.Source, impact.Position, impact.Knockback, impact.ActivationId);
                    }
                }
                state.Emit(world.Tick, "review-impact", impact.Source, impact.Id, impact.Position, impact.Position, impact.Radius);
                state.ResolveChild(impact.ActivationId);
                state.PendingImpacts.RemoveAt(i);
            }
        }
    }
}
