using System;
using System.Linq;

namespace SowSiege.Core
{
    internal sealed class WaveWorkSystem
    {
        private readonly ContentCatalog catalog;
        private readonly WorldState world;
        private readonly InteractiveState? interactive;
        private WaveRuntimeState State => world.WaveRuntime!;
        private WaveRuntimeDefinition Definition => catalog.WaveRuntime!;

        internal WaveWorkSystem(ContentCatalog catalog, WorldState world, InteractiveState? interactive)
        { this.catalog = catalog; this.world = world; this.interactive = interactive; }

        private bool Has(WaveItemKind kind) => State.Items.Any(id => Definition.Items[id].Kind == kind);
        private WaveGearDefinition Gear(string source)
        {
            if (Definition.Gear.TryGetValue(source, out var gear)) { return gear; }
            var evolution = Definition.Evolutions[source];
            return Definition.Gear[evolution.InputIds.First(id => Definition.Gear[id].Kind >= WaveAttackKind.SeedFan)];
        }
        private bool Near(Position a, Position b, int radius) => a.DistanceSquared(b) <= (long)radius * radius;
        private Position Clamp(Position p) => new(Math.Clamp(p.X, 0, catalog.Tuning.World.Map.Width), Math.Clamp(p.Y, 0, catalog.Tuning.World.Map.Height));
        private void Emit(string kind, string source, int id, Position at, int amount = 0) => State.Emit(world.Tick, kind, source, id, at, at, amount);

        internal void Activate(WaveGearDefinition gear, Position position, Position direction)
        {
            var at = Clamp(position.MoveToward(new(position.X + direction.X * gear.Range, position.Y + direction.Y * gear.Range), Math.Max(1, gear.Range / WaveGeometry.MidpointDivisor)));
            if (Definition.Evolutions.TryGetValue(gear.Id, out var evolution) && evolution.Kind == WaveEvolutionKind.ShelteredPlot)
            {
                var builder = Definition.Gear[evolution.InputIds.First(id => Definition.Gear[id].Kind == WaveAttackKind.ConstructionSlam)];
                var seed = Definition.Gear[evolution.InputIds.First(id => Definition.Gear[id].Kind == WaveAttackKind.SeedFan)];
                var building = Build(builder, at);
                if (building is not null && !State.Work.Any(w => w.Kind == "grain" && w.Health > 0 && w.ParentId == building.Id))
                { var plot = Plant(seed, Clamp(new(building.Position.X + Math.Max(1, seed.WorkRadius / WaveGeometry.MidpointDivisor), building.Position.Y))); if (plot is not null) { plot.ParentId = building.Id; } }
                return;
            }
            switch (gear.Kind)
            {
                case WaveAttackKind.SeedFan: Plant(Gear(gear.Id), at); break;
                case WaveAttackKind.WaterFan:
                    var pool = State.Work.Where(w => w.Kind == "water" && Near(w.Position, at, Math.Max(1, gear.WorkRadius))).OrderBy(w => w.Id).FirstOrDefault();
                    if (pool is null && State.Work.Count(w => w.Kind == "water") < gear.Capacity) { pool = Add(gear, "water", at); }
                    if (pool is not null && pool.Health <= 0) { pool.Health = 1; Emit("pool-repaired", gear.Id, pool.Id, pool.Position); }
                    if (State.Water > 0 && pool is not null)
                    { State.Water--; pool.WetUntil = world.Tick + Definition.WetTicks; Emit("water-spent-attack", gear.Id, pool.Id, at, 1); }
                    else { Emit("dry-strike", gear.Id, -1, at); }
                    break;
                case WaveAttackKind.ConstructionSlam: Build(gear, at); break;
                case WaveAttackKind.MusterWave: Recruit(gear); break;
            }
        }

        internal void PlantSweep(string seedSource, Position origin, Position direction, int range, int count)
        {
            var gear = Gear(seedSource);
            var forward = origin.MoveToward(new(origin.X + direction.X * range, origin.Y + direction.Y * range), Math.Max(1, range / WaveGeometry.MidpointDivisor));
            var dx = forward.X - origin.X; var dy = forward.Y - origin.Y;
            for (var i = 0; i < Math.Max(1, count); i++)
            {
                var offset = count <= 1 ? 0 : (i * WaveGeometry.MidpointDivisor - count + 1);
                var denominator = Math.Max(1, count - 1);
                Plant(gear, Clamp(new(forward.X - (int)((long)dy * offset / denominator), forward.Y + (int)((long)dx * offset / denominator))));
            }
        }

        private WaveWork Add(WaveGearDefinition gear, string kind, Position at)
        {
            var work = new WaveWork { Id = world.AllocateId(), Source = gear.Id, Kind = kind, Position = at, Required = Math.Max(1, gear.WorkTicks), Health = Math.Max(1, gear.Capacity), Cycle = State.NextCycle++ };
            if (kind == "grain") { work.ReadyTick = world.Tick + Definition.DryAfterTicks; }
            State.Work.Add(work); Emit("work-created", gear.Id, work.Id, at); return work;
        }
        private WaveWork? Plant(WaveGearDefinition gear, Position at)
        {
            var live = State.Work.Where(w => w.Kind == "grain" && w.Health > 0).ToArray();
            if (live.Length >= gear.Capacity || live.Any(w => Near(w.Position, at, Math.Max(1, catalog.Tuning.World.Farms.Spacing)))) { return null; }
            // A genuinely new sowing starts a new cycle; destroyed or repaired objects never reset theirs.
            return Add(gear, "grain", at);
        }
        private WaveWork? Build(WaveGearDefinition gear, Position at)
        {
            var existing = State.Work.Where(w => w.Kind == "building" && Near(w.Position, at, gear.WorkRadius)).OrderBy(w => w.Id).FirstOrDefault();
            if (existing is not null)
            {
                if (existing.Health <= 0) { existing.Health = Math.Max(1, gear.Capacity); existing.Complete = false; existing.Progress = 0; existing.ReadyTick = -1; Emit("repair-start", existing.Source, existing.Id, existing.Position); }
                return existing;
            }
            if (State.Work.Count(w => w.Kind == "building") >= gear.Capacity) { return null; }
            return Add(gear, "building", at);
        }

        internal void Tick()
        {
            TickRain();
            foreach (var w in State.Work.Where(w => w.Health > 0).ToArray())
            {
                var gear = Gear(w.Source);
                if (w.Kind == "grain")
                {
                    if (w.DormantUntil > world.Tick) { continue; }
                    if (Definition.DryAfterTicks > 0 && !w.Complete && world.Tick >= w.ReadyTick) { w.Dry = true; }
                    if (w.Dry) { continue; }
                    var roof = w.ParentId < 0 ? null : State.Work.FirstOrDefault(b => b.Id == w.ParentId);
                    if (w.ParentId >= 0 && (roof is null || !roof.Complete || roof.Health <= 0)) { w.Protected = false; continue; }
                    w.Protected = roof is not null && !State.Completed.Contains("roof-used:" + roof.Id);
                    if (!w.Complete && ++w.Progress >= w.Required) { w.Complete = true; Emit("growth-complete", w.Source, w.Id, w.Position); }
                    if (w.Complete && Near(world.Lord, w.Position, Definition.PickupRadius)) { Harvest(w); }
                }
                else if (w.Kind == "building") { TickBuilding(w, gear); }
                else if (w.Kind == "water") { TickWater(w, gear); }
            }
            TickCarriedWater(); TickGroups(); TraceWorkPath(); Collect();
        }
        private void TickBuilding(WaveWork w, WaveGearDefinition gear)
        {
            if (w.Complete && world.Tick >= w.ReadyTick) { Brace(w, gear); }
            if (!Near(world.Lord, w.Position, gear.WorkRadius)) { return; }
            if (!w.Complete)
            {
                if (++w.Progress < w.Required) { return; }
                w.Complete = true; w.Progress = 0; w.Health = Math.Max(1, gear.Capacity);
                if (w.ReadyTick == -1) { State.RepairCompleted = true; State.Completed.Remove("roof-used:" + w.Id); }
                w.ReadyTick = world.Tick; Emit("building-complete", w.Source, w.Id, w.Position);
            }
            if (!w.ShipmentActive && State.Timber > 0 && Near(w.Position, State.TimberOrigin, gear.WorkRadius))
            { State.Timber--; w.ShipmentActive = true; Emit("timber-reserved", w.Source, w.Id, w.Position, 1); }
            if (!w.ShipmentActive || ++w.Progress < w.Required) { return; }
            if (Reward(w.Source, w.Id, w.Cycle, "shipment", w.Position, gear.RewardExperience)) { State.ProcessedTimber++; }
            w.ShipmentActive = false; w.Progress = 0; w.Cycle = State.NextCycle++;
            Emit("shipment-complete", w.Source, w.Id, w.Position);
        }
        private void Brace(WaveWork building, WaveGearDefinition gear)
        {
            var nearest = world.Enemies.Where(e => e.Health > 0 && Near(e.Position, building.Position, gear.Range)).OrderBy(e => e.Position.DistanceSquared(building.Position)).ThenBy(e => e.Id).FirstOrDefault();
            if (nearest is null) { return; }
            var aim = new Position(nearest.Position.X - building.Position.X, nearest.Position.Y - building.Position.Y);
            if (aim == new Position(0, 0)) { aim = State.Facing; }
            var targets = world.Enemies.Where(e => e.Health > 0 && WaveRuntimeSystem.InArc(building.Position, aim, e.Position, gear.Range)).OrderBy(e => e.Id).ToArray();
            var activation = State.BeginActivation("building:" + building.Source, 0, world.Tick);
            State.Emit(world.Tick, "building-brace-swing", building.Source, building.Id, building.Position, nearest.Position, gear.Range);
            foreach (var target in targets)
            {
                var damage = Math.Min(target.Health, gear.Damage); target.Health -= damage; world.WeaponDamage += damage; State.RecordDamage(activation, gear.Damage, damage);
                if (gear.Knockback > 0) { target.Position = Clamp(new(target.Position.X + Math.Sign(target.Position.X - building.Position.X) * gear.Knockback, target.Position.Y + Math.Sign(target.Position.Y - building.Position.Y) * gear.Knockback)); }
                Emit("building-brace-hit", building.Source, target.Id, target.Position, damage);
            }
            State.ResolveActivation(activation); building.ReadyTick = world.Tick + gear.CooldownTicks;
        }
        private void TickRain()
        {
            if (Definition.WaterRefillTicks <= 0 || world.Tick <= 0 || world.Tick % Definition.WaterRefillTicks != 0 || !State.Completed.Add("rain:" + world.Tick)) { return; }
            foreach (var plot in State.Work.Where(w => w.Kind == "grain" && w.Health > 0 && !w.Complete))
            { plot.Dry = false; plot.ReadyTick = world.Tick + Definition.DryAfterTicks; }
            var pool = State.Work.FirstOrDefault(w => w.Kind == "water" && w.Health > 0);
            if (pool is null) { Emit("rain-fall", "", -1, world.Lord); return; }
            var fill = Definition.WaterCapacity - State.Water; State.Water += fill;
            Emit("rain-fill", pool.Source, pool.Id, pool.Position, fill);
        }
        private void TickWater(WaveWork pool, WaveGearDefinition gear)
        {
            if (pool.WetUntil > world.Tick)
            {
                foreach (var enemy in world.Enemies.Where(e => e.Health > 0 && Near(e.Position, pool.Position, gear.WorkRadius)))
                { if (!State.EnemyActions.TryGetValue(enemy.Id, out var action)) { action = new(); State.EnemyActions[enemy.Id] = action; } action.WetUntil = world.Tick + Definition.WetTicks; }
            }
            if (State.Water <= 0) { return; }
            var plot = State.Work.Where(w => w.Kind == "grain" && w.Health > 0 && !w.Complete && w.Dry && Near(w.Position, pool.Position, gear.WorkRadius)).OrderBy(w => w.Id).FirstOrDefault();
            if (plot is null) { return; }
            State.Water--; Irrigate(plot, pool.Source, pool.Id, gear.RewardExperience); pool.WetUntil = world.Tick + Definition.WetTicks;
        }
        private void Irrigate(WaveWork plot, string source, int instance, int experience)
        {
            if (!plot.Dry) { return; }
            plot.Dry = false; plot.Irrigated = true; plot.ReadyTick = world.Tick + Definition.DryAfterTicks;
            Reward(source, plot.Id, plot.Cycle, "irrigation", plot.Position, experience); Emit("irrigation-complete", source, plot.Id, plot.Position, 1);
        }
        private void TickCarriedWater()
        {
            if (!Has(WaveItemKind.CarryWater)) { return; }
            var gear = Definition.Gear.Values.FirstOrDefault(g => g.Kind == WaveAttackKind.WaterFan);
            if (gear is null) { return; }
            if (State.CarriedWater == 0 && State.Water > 0)
            {
                var pool = State.Work.FirstOrDefault(w => w.Kind == "water" && w.Health > 0 && w.WetUntil > world.Tick && Near(w.Position, world.Lord, Definition.PickupRadius));
                if (pool is not null) { State.Water--; State.CarriedWater = 1; Emit("water-carried", gear.Id, pool.Id, pool.Position, 1); }
            }
            if (State.CarriedWater == 0) { return; }
            var plot = State.Work.Where(w => w.Kind == "grain" && w.Health > 0 && !w.Complete && w.Dry && Near(w.Position, world.Lord, Definition.PickupRadius)).OrderBy(w => w.Id).FirstOrDefault();
            if (plot is null) { return; }
            State.CarriedWater--; Irrigate(plot, gear.Id, -1, gear.RewardExperience);
        }

        internal void Harvest(WaveWork work)
        {
            if (work.Kind != "grain" || work.Health <= 0 || !work.Complete) { return; }
            if (!Reward(work.Source, work.Id, work.Cycle, "growth", work.Position, Gear(work.Source).RewardExperience)) { return; }
            work.Health = 0; world.Harvests++; world.Food++;
            State.HarvestNearBuilding |= State.Work.Any(w => w.Kind == "building" && w.Health > 0 && w.Complete && Near(w.Position, work.Position, Gear(w.Source).WorkRadius));
            if (Has(WaveItemKind.HarvestGuard))
            {
                var group = State.Groups.Where(g => g.Health > 0 && (g.Phase == "engaging" || g.Phase == "guarding")).OrderBy(g => g.Id).FirstOrDefault();
                if (group is not null) { group.Destination = work.Position; group.Phase = "guarding"; Emit("harvest-guard", group.Source, group.Id, work.Position); }
            }
            Emit("harvest-complete", work.Source, work.Id, work.Position);
        }
        private bool Reward(string source, int instance, int cycle, string kind, Position at, int experience)
        {
            var key = kind + ":" + source + ":" + instance + ":" + cycle;
            if (!State.Completed.Add(key)) { return false; }
            State.Rewards.Add(new() { Id = world.AllocateId(), Source = source, CompletionKey = key, Experience = experience, Position = at });
            Emit("reward-created", source, instance, at, experience); return true;
        }
        private void Collect()
        {
            var radius = Definition.PickupRadius + State.Items.Where(id => Definition.Items[id].Kind == WaveItemKind.PickupRadius).Sum(id => Definition.Items[id].Amount);
            foreach (var reward in State.Rewards.Where(r => Near(r.Position, world.Lord, radius)).ToArray())
            { world.Experience += reward.Experience; world.HarvestExperience += reward.Experience; State.Rewards.Remove(reward); Emit("reward-collected", reward.Source, reward.Id, reward.Position, reward.Experience); interactive?.Experience(world.Tick, PresentationKind.HarvestExperience, reward.Source, reward.Position, reward.Experience); }
        }
        private void Recruit(WaveGearDefinition gear)
        {
            if (!world.Enemies.Any(e => e.Health > 0 && Near(e.Position, world.Lord, gear.Range))) { return; }
            var group = State.Groups.Where(g => g.Source == gear.Id && g.Health > 0 && g.Phase == "idle").OrderBy(g => g.Id).FirstOrDefault();
            if (group is null)
            {
                if (State.AvailableWorkers <= 0 || State.Groups.Count(g => g.Health > 0) >= Definition.GroupCap) { return; }
                State.AvailableWorkers--; group = new() { Id = world.AllocateId(), Source = gear.Id, Position = world.Lord, Health = Math.Max(1, gear.Capacity) }; State.Groups.Add(group);
            }
            else
            {
                if (State.AvailableWorkers <= 0) { return; }
                State.AvailableWorkers--;
            }
            group.Mission = State.NextMission++; group.Participants.Clear(); group.Engaged = false; group.Formation = "advance"; group.Phase = "engaging"; group.Destination = world.Lord;
            if (Has(WaveItemKind.FieldMeal) && world.Food > 0) { world.Food--; group.ReservedFood = 1; Emit("food-reserved", gear.Id, group.Id, group.Position, 1); }
            Emit("mission-start", gear.Id, group.Id, group.Position);
        }
        private void TickGroups()
        {
            foreach (var group in State.Groups.Where(g => g.Health > 0))
            {
                var gear = Gear(group.Source);
                if (group.Phase == "idle") { continue; }
                if (group.Phase == "returning")
                {
                    group.Position = group.Position.MoveToward(world.Lord, Math.Max(1, gear.Speed));
                    if (!Near(group.Position, world.Lord, Definition.PickupRadius)) { continue; }
                    if (group.Engaged && State.Completed.Contains("mission-kill:" + group.Id + ":" + group.Mission)) { Reward(group.Source, group.Id, group.Mission, "mission", group.Position, gear.RewardExperience); Emit("mission-return", group.Source, group.Id, group.Position); }
                    world.Food += group.ReservedFood; group.ReservedFood = 0; group.Phase = "idle"; group.Formation = "advance"; State.AvailableWorkers++; continue;
                }
                var anchor = group.Phase == "guarding" ? group.Destination : world.Lord;
                var enemy = world.Enemies.Where(e => e.Health > 0 && Near(e.Position, anchor, gear.Range)).OrderBy(e => e.Position.DistanceSquared(group.Position)).ThenBy(e => e.Id).FirstOrDefault();
                if (enemy is null) { group.Phase = "returning"; continue; }
                if (group.Training > 0 && world.Tick < group.ReadyTick)
                {
                    group.Formation = "cover";
                    var returning = State.Groups.Where(g => g.Id != group.Id && g.Health > 0 && g.Phase == "returning").OrderBy(g => g.Position.DistanceSquared(group.Position)).ThenBy(g => g.Id).FirstOrDefault();
                    var defended = returning?.Position ?? anchor;
                    var cover = defended.MoveToward(enemy.Position, Math.Max(1, gear.WorkRadius / WaveGeometry.MidpointDivisor));
                    group.Position = group.Position.MoveToward(cover, Math.Max(1, gear.Speed));
                    continue;
                }
                group.Formation = "advance";
                group.Position = group.Position.MoveToward(enemy.Position, Math.Max(1, gear.Speed));
                if (!Near(group.Position, enemy.Position, gear.WorkRadius) || world.Tick < group.ReadyTick) { continue; }
                group.ReadyTick = world.Tick + gear.CooldownTicks; group.Engaged = true; group.Participants.Add(enemy.Id);
                var activation = State.BeginActivation("group:" + group.Source, 0, world.Tick);
                var damage = Math.Min(enemy.Health, gear.Damage); enemy.Health -= damage; world.AllyDamage += damage; State.RecordDamage(activation, gear.Damage, damage); State.ResolveActivation(activation);
                if (group.Training > 0) { group.FrontRank = 1 - group.FrontRank; group.Formation = "cover"; Emit("group-rank-swapped", group.Source, group.Id, group.Position, group.FrontRank); }
                Emit(group.Training > 0 ? "trained-group-hit" : "group-hit", group.Source, enemy.Id, enemy.Position, damage);
                if (group.ReservedFood > 0) { group.Health = Math.Min(gear.Capacity, group.Health + group.ReservedFood); group.ReservedFood = 0; Emit("field-meal", group.Source, group.Id, group.Position, 1); }
            }
        }
        internal void OnKill(EnemyState enemy)
        {
            foreach (var group in State.Groups.Where(g => g.Health > 0 && g.Participants.Contains(enemy.Id)))
            { if (State.Completed.Add("training:" + group.Id + ":" + enemy.Id)) { group.Training++; State.Completed.Add("mission-kill:" + group.Id + ":" + group.Mission); Emit("group-trained", group.Source, group.Id, group.Position, 1); } }
        }
        private void TraceWorkPath()
        {
            if (State.Paths.Count >= Definition.PathCapacity || !State.Work.Any(w => w.Health > 0 && Near(w.Position, world.Lord, Gear(w.Source).WorkRadius))) { return; }
            if (State.Paths.Any(p => Near(p, world.Lord, Definition.PathSpacing))) { return; }
            State.Paths.Add(world.Lord); Emit("work-path", "", -1, world.Lord);
        }
    }
}
