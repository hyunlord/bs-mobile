using System;
namespace SowSiege.Core
{
    internal sealed class WavePendingImpact
    {
        public int Id, Tick, Radius, Damage, Knockback, ActivationId;
        public string Source = "";
        public Position Position;
    }
    internal sealed partial class WaveRuntimeSystem
    {
        private void ScheduleReviewImpacts(WaveGearDefinition gear)
        {
            var c = gear.Composition!;
            if (c.Version != 1 || c.Placement is not ("nearest-target" or "forward-line") || c.DelayTicks < 1 || c.StepTicks < 0 || c.Spacing < 0 || c.Radius < 1 || gear.Count < 1 || gear.Count > WaveAttackComposition.MaximumImpacts)
            { throw new ArgumentException("Unsupported review impact composition."); }
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
        private void TickReviewImpacts()
        {
            for (var i = 0; i < state.PendingImpacts.Count;)
            {
                var impact = state.PendingImpacts[i];
                if (impact.Tick > world.Tick) { i++; continue; }
                queries.ByDistance(secondaryTargets, impact.Position, impact.Radius, impact.Position);
                foreach (var enemy in secondaryTargets)
                {
                    if (state.Activations[impact.ActivationId].HitTargets.Add(enemy.Id))
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
