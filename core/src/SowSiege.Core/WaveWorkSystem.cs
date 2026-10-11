using System;
using System.Collections.Generic;

namespace SowSiege.Core
{
    internal sealed class WaveWorkSystem
    {
        private readonly ContentCatalog catalog;
        private readonly WorldState world;
        private readonly InteractiveState? interactive;
        private readonly WavePrimitiveModules modules;
        private readonly WaveItemSubscriptions subscriptions;
        private readonly WaveGrowthPrimitives growth;
        private readonly WaveCompletionLedger completions;
        private readonly WaveEnemyQueries queries;
        private readonly bool ownsQueries;
        private readonly List<EnemyState> enemyCandidates = new();
        private readonly Action<WaveWork, string, int, int> irrigate;
        private WaveRuntimeState State => world.WaveRuntime!;
        private WaveRuntimeDefinition Definition => catalog.WaveRuntime!;

        internal WaveWorkSystem(ContentCatalog catalog, WorldState world, InteractiveState? interactive, WavePrimitiveModules? modules = null, WaveItemSubscriptions? subscriptions = null, WaveEnemyQueries? queries = null)
        { this.catalog = catalog; this.world = world; this.interactive = interactive; this.modules = modules ?? new(catalog.WaveRuntime!); this.subscriptions = subscriptions ?? new(catalog, world, this.modules); ownsQueries = queries is null; this.queries = queries ?? new(world, catalog.Tuning.World.Farms.Spacing, interactive?.Field is not null); growth = new(world, this.modules); completions = new(world, this.modules); irrigate = Irrigate; }

        private WaveGearDefinition Gear(string source)
        {
            if (Definition.Gear.TryGetValue(source, out var gear)) { return gear; }
            var evolution = Definition.Evolutions[source];
            foreach (var id in evolution.InputIds) { if (modules.Has(id, "unit:remnant-create")) { return Definition.Gear[id]; } }
            throw new InvalidOperationException("Evolution has no remnant creation input.");
        }
        private bool Near(Position a, Position b, int radius) => a.DistanceSquared(b) <= (long)radius * radius;
        private Position Clamp(Position p) => interactive?.Field is { } field ? field.Constrain(p) : new(Math.Clamp(p.X, 0, catalog.Tuning.World.Map.Width), Math.Clamp(p.Y, 0, catalog.Tuning.World.Map.Height));
        private void Emit(string kind, string source, int id, Position at, int amount = 0) => State.Emit(world.Tick, kind, source, id, at, at, amount);

        internal void Activate(WaveGearDefinition gear, Position position, Position direction)
        {
            if (ownsQueries) { queries.Rebuild(); }
            var at = Clamp(position.MoveToward(new(position.X + direction.X * gear.Range, position.Y + direction.Y * gear.Range), Math.Max(1, gear.Range / WaveGeometry.MidpointDivisor)));
            if (Definition.Evolutions.TryGetValue(gear.Id, out var evolution) && modules.Has(evolution.Id, "unit:paired-growth"))
            {
                var builder = PairedInput(evolution, "target", "building");
                var seed = PairedInput(evolution, "remnantKey", "grain-patch");
                var building = Build(builder, at);
                var hasPlot = false;
                if (building is not null)
                { foreach (var work in State.Work) { if (work.Kind == "grain" && work.Health > 0 && work.ParentId == building.Id) { hasPlot = true; break; } } }
                if (building is not null && !hasPlot)
                { var plot = Plant(seed, Clamp(new(building.Position.X + Math.Max(1, seed.WorkRadius / WaveGeometry.MidpointDivisor), building.Position.Y))); if (plot is not null) { plot.ParentId = building.Id; } }
                return;
            }
            switch (modules[gear.Id].Value("unit:remnant-create", "remnantKey"))
            {
                case "grain-patch": Plant(Gear(gear.Id), at); break;
                case "seasonal-pool":
                    WaveWork? pool = null;
                    var pools = 0;
                    foreach (var work in State.Work)
                    {
                        if (work.Kind != "water") { continue; }
                        pools++;
                        if (Near(work.Position, at, Math.Max(1, gear.WorkRadius)) && (pool is null || work.Id < pool.Id)) { pool = work; }
                    }
                    if (pool is null && pools < gear.Capacity) { pool = Add(gear, "water", at); }
                    if (pool is not null && pool.Health <= 0) { pool.Health = 1; Emit("pool-repaired", gear.Id, pool.Id, pool.Position); }
                    if (State.Water > 0 && pool is not null)
                    { State.Water--; pool.WetUntil = world.Tick + Definition.WetTicks; Emit("water-spent-attack", gear.Id, pool.Id, at, 1); }
                    else { Emit("dry-strike", gear.Id, -1, at); }
                    break;
                case "joinery-frame": Build(gear, at); break;
                case "levy-company": Recruit(gear); break;
                default: throw new ArgumentException("Unsupported remnant creation unit.");
            }
        }

        private WaveGearDefinition PairedInput(WaveEvolutionDefinition evolution, string parameter, string value)
        {
            foreach (var id in evolution.InputIds)
            { if (modules.Is(id, "unit:remnant-create", parameter, value)) { return Definition.Gear[id]; } }
            throw new InvalidOperationException("Evolution has no matching remnant creation input.");
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
            var live = 0;
            foreach (var work in State.Work)
            {
                if (work.Kind != "grain" || work.Health <= 0) { continue; }
                if (Near(work.Position, at, Math.Max(1, catalog.Tuning.World.Farms.Spacing))) { return null; }
                live++;
            }
            if (live >= gear.Capacity) { return null; }
            // A genuinely new sowing starts a new cycle; destroyed or repaired objects never reset theirs.
            return Add(gear, "grain", at);
        }
        private WaveWork? Build(WaveGearDefinition gear, Position at)
        {
            WaveWork? existing = null;
            var buildings = 0;
            foreach (var work in State.Work)
            {
                if (work.Kind != "building") { continue; }
                buildings++;
                if (Near(work.Position, at, gear.WorkRadius) && (existing is null || work.Id < existing.Id)) { existing = work; }
            }
            if (existing is not null)
            {
                if (existing.Health <= 0) { existing.Health = Math.Max(1, gear.Capacity); existing.Complete = false; existing.Progress = 0; existing.ReadyTick = -1; Emit("repair-start", existing.Source, existing.Id, existing.Position); }
                return existing;
            }
            if (buildings >= gear.Capacity) { return null; }
            return Add(gear, "building", at);
        }

        internal void Tick()
        {
            if (ownsQueries) { queries.Rebuild(); }
            TickRain();
            foreach (var w in State.Work)
            {
                if (w.Health <= 0) { continue; }
                var gear = Gear(w.Source);
                if (w.Kind == "grain")
                {
                    if (w.DormantUntil > world.Tick) { continue; }
                    if (Definition.DryAfterTicks > 0 && !w.Complete && world.Tick >= w.ReadyTick) { w.Dry = true; }
                    if (w.Dry) { continue; }
                    WaveWork? roof = null;
                    if (w.ParentId >= 0)
                    { foreach (var candidate in State.Work) { if (candidate.Id == w.ParentId) { roof = candidate; break; } } }
                    if (w.ParentId >= 0 && (roof is null || !roof.Complete || roof.Health <= 0)) { w.Protected = false; continue; }
                    w.Protected = roof is not null && !State.Completed.Contains("roof-used:" + roof.Id);
                    if (!w.Complete && growth.AdvanceTime(w, gear)) { w.Complete = true; Emit("growth-complete", w.Source, w.Id, w.Position); }
                    if (w.Complete && Near(world.Lord, w.Position, Definition.PickupRadius)) { Harvest(w); }
                }
                else if (w.Kind == "building") { TickBuilding(w, gear); }
                else if (w.Kind == "water") { TickWater(w, gear); }
            }
            TickCarriedWater(); TickGroups(); TraceWorkPath(); Collect();
        }
        private void TickBuilding(WaveWork w, WaveGearDefinition gear)
        {
            if (w.Complete && world.Tick >= w.ReadyTick && modules.Has(w.Source, "unit:completed-structure-attack")) { Brace(w, gear); }
            if (!Near(world.Lord, w.Position, gear.WorkRadius)) { return; }
            if (!w.Complete)
            {
                if (!growth.AdvanceTime(w, gear)) { return; }
                w.Complete = true; w.Progress = 0; w.Health = Math.Max(1, gear.Capacity);
                if (w.ReadyTick == -1) { State.RepairCompleted = true; State.Completed.Remove("roof-used:" + w.Id); }
                w.ReadyTick = world.Tick; Emit("building-complete", w.Source, w.Id, w.Position);
            }
            if (!w.ShipmentActive && State.Timber > 0 && Near(w.Position, State.TimberOrigin, gear.WorkRadius))
            { State.Timber--; w.ShipmentActive = true; Emit("timber-reserved", w.Source, w.Id, w.Position, 1); }
            if (!w.ShipmentActive || !growth.AdvanceTime(w, gear)) { return; }
            if (Reward(w.Source, w.Id, w.Cycle, "shipment", w.Position, gear.RewardExperience)) { State.ProcessedTimber++; }
            w.ShipmentActive = false; w.Progress = 0; w.Cycle = State.NextCycle++;
            Emit("shipment-complete", w.Source, w.Id, w.Position);
        }
        private void Brace(WaveWork building, WaveGearDefinition gear)
        {
            queries.ByDistance(enemyCandidates, building.Position, gear.Range, building.Position);
            var nearest = enemyCandidates.Count == 0 ? null : enemyCandidates[0];
            if (nearest is null) { return; }
            var aim = new Position(nearest.Position.X - building.Position.X, nearest.Position.Y - building.Position.Y);
            if (aim == new Position(0, 0)) { aim = State.Facing; }
            queries.ById(enemyCandidates, building.Position, gear.Range);
            var activation = State.BeginActivation("building:" + building.Source, 0, world.Tick);
            State.Emit(world.Tick, "building-brace-swing", building.Source, building.Id, building.Position, nearest.Position, gear.Range);
            foreach (var target in enemyCandidates)
            {
                if (!WaveRuntimeSystem.InArc(building.Position, aim, target.Position, gear.Range)) { continue; }
                var damage = Math.Min(target.Health, gear.Damage); target.Health -= damage; world.WeaponDamage += damage; State.RecordDamage(activation, gear.Damage, damage);
                if (gear.Knockback > 0) { queries.Move(target, Clamp(new(target.Position.X + Math.Sign(target.Position.X - building.Position.X) * gear.Knockback, target.Position.Y + Math.Sign(target.Position.Y - building.Position.Y) * gear.Knockback))); }
                Emit("building-brace-hit", building.Source, target.Id, target.Position, damage);
            }
            State.ResolveActivation(activation); building.ReadyTick = world.Tick + gear.CooldownTicks;
        }
        private void TickRain()
        {
            if (Definition.WaterRefillTicks <= 0 || world.Tick <= 0 || world.Tick % Definition.WaterRefillTicks != 0 || !State.Completed.Add("rain:" + world.Tick)) { return; }
            WaveWork? pool = null;
            foreach (var plot in State.Work)
            {
                if (plot.Kind == "grain" && plot.Health > 0 && !plot.Complete) { plot.Dry = false; plot.ReadyTick = world.Tick + Definition.DryAfterTicks; }
                if (pool is null && plot.Kind == "water" && plot.Health > 0) { pool = plot; }
            }
            if (pool is null) { Emit("rain-fall", "", -1, world.Lord); return; }
            var fill = Definition.WaterCapacity - State.Water; State.Water += fill;
            Emit("rain-fill", pool.Source, pool.Id, pool.Position, fill);
        }
        private void TickWater(WaveWork pool, WaveGearDefinition gear)
        {
            if (pool.WetUntil > world.Tick && modules.Has(pool.Source, "unit:status-apply"))
            {
                queries.ByWorldOrder(enemyCandidates, pool.Position, gear.WorkRadius);
                foreach (var enemy in enemyCandidates)
                { if (!State.EnemyActions.TryGetValue(enemy.Id, out var action)) { action = new(); State.EnemyActions[enemy.Id] = action; } action.WetUntil = world.Tick + Definition.WetTicks; }
            }
            if (State.Water <= 0) { return; }
            WaveWork? plot = null;
            foreach (var candidate in State.Work)
            { if (candidate.Kind == "grain" && candidate.Health > 0 && !candidate.Complete && candidate.Dry && Near(candidate.Position, pool.Position, gear.WorkRadius) && (plot is null || candidate.Id < plot.Id)) { plot = candidate; } }
            if (plot is null) { return; }
            State.Water--; Irrigate(plot, pool.Source, pool.Id, gear.RewardExperience); pool.WetUntil = world.Tick + Definition.WetTicks;
        }
        private void Irrigate(WaveWork plot, string source, int instance, int experience)
        {
            if (!plot.Dry) { return; }
            plot.Dry = false; plot.Irrigated = true; plot.ReadyTick = world.Tick + Definition.DryAfterTicks;
            Reward(source, plot.Id, plot.Cycle, "irrigation", plot.Position, experience); Emit("irrigation-complete", source, plot.Id, plot.Position, 1);
        }
        private void TickCarriedWater() => subscriptions.CarryWater(irrigate);

        internal void Harvest(WaveWork work)
        {
            if (work.Kind != "grain" || work.Health <= 0 || !work.Complete) { return; }
            if (!Reward(work.Source, work.Id, work.Cycle, "growth", work.Position, Gear(work.Source).RewardExperience)) { return; }
            work.Health = 0; world.Harvests++; world.Food++;
            foreach (var building in State.Work)
            { if (building.Kind == "building" && building.Health > 0 && building.Complete && Near(building.Position, work.Position, Gear(building.Source).WorkRadius)) { State.HarvestNearBuilding = true; break; } }
            subscriptions.Harvest(work);
            Emit("harvest-complete", work.Source, work.Id, work.Position);
        }
        private bool Reward(string source, int instance, int cycle, string kind, Position at, int experience) => completions.Claim(source, instance, cycle, kind, at, experience);
        private void Collect()
        {
            var radius = Definition.PickupRadius + subscriptions.Modifier("pickup-radius");
            for (var index = 0; index < State.Rewards.Count;)
            {
                var reward = State.Rewards[index];
                if (!Near(reward.Position, world.Lord, radius)) { index++; continue; }
                world.Experience += reward.Experience; world.HarvestExperience += reward.Experience; State.Rewards.RemoveAt(index); Emit("reward-collected", reward.Source, reward.Id, reward.Position, reward.Experience); interactive?.Experience(world.Tick, PresentationKind.HarvestExperience, reward.Source, reward.Position, reward.Experience);
            }
        }
        private void Recruit(WaveGearDefinition gear)
        {
            queries.Collect(enemyCandidates, world.Lord, gear.Range);
            if (enemyCandidates.Count == 0) { return; }
            WaveGroup? group = null;
            var liveGroups = 0;
            foreach (var candidate in State.Groups)
            {
                if (candidate.Health <= 0) { continue; }
                liveGroups++;
                if (candidate.Source == gear.Id && candidate.Phase == "idle" && (group is null || candidate.Id < group.Id)) { group = candidate; }
            }
            if (group is null)
            {
                if (subscriptions.WaitForReturnedGroup(gear.Id) || State.AvailableWorkers <= 0 || liveGroups >= Definition.GroupCap) { return; }
                State.AvailableWorkers--; group = new() { Id = world.AllocateId(), Source = gear.Id, Position = world.Lord, Health = Math.Max(1, gear.Capacity) }; State.Groups.Add(group);
            }
            else
            {
                if (State.AvailableWorkers <= 0) { return; }
                State.AvailableWorkers--;
            }
            group.Mission = State.NextMission++; group.Participants.Clear(); group.Engaged = false; group.Formation = "advance"; group.Phase = "engaging"; group.Destination = world.Lord;
            subscriptions.ReserveMissionFood(gear, group);
            Emit("mission-start", gear.Id, group.Id, group.Position);
        }
        private void TickGroups()
        {
            foreach (var group in State.Groups)
            {
                if (group.Health <= 0) { continue; }
                var gear = Gear(group.Source);
                if (group.Phase == "idle") { continue; }
                if (group.Phase == "returning")
                {
                    group.Position = group.Position.MoveToward(world.Lord, Math.Max(1, gear.Speed));
                    if (!Near(group.Position, world.Lord, Definition.PickupRadius)) { continue; }
                    if (group.Engaged && State.Completed.Contains("mission-kill:" + group.Id + ":" + group.Mission)) { Reward(group.Source, group.Id, group.Mission, "mission", group.Position, gear.RewardExperience); Emit("mission-return", group.Source, group.Id, group.Position); }
                    subscriptions.ReturnMissionFood(group); group.Phase = "idle"; group.Formation = "advance"; State.AvailableWorkers++; continue;
                }
                var anchor = group.Phase == "guarding" ? group.Destination : world.Lord;
                queries.ByDistance(enemyCandidates, anchor, gear.Range, group.Position);
                var enemy = enemyCandidates.Count == 0 ? null : enemyCandidates[0];
                if (enemy is null) { group.Phase = "returning"; continue; }
                if (group.Training > 0 && world.Tick < group.ReadyTick)
                {
                    group.Formation = "cover";
                    WaveGroup? returning = null;
                    var nearestDistance = long.MaxValue;
                    foreach (var candidate in State.Groups)
                    {
                        if (candidate.Id == group.Id || candidate.Health <= 0 || candidate.Phase != "returning") { continue; }
                        var distance = candidate.Position.DistanceSquared(group.Position);
                        if (returning is null || distance < nearestDistance || distance == nearestDistance && candidate.Id < returning.Id)
                        { returning = candidate; nearestDistance = distance; }
                    }
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
                subscriptions.ConsumeMissionFood(gear, group);
            }
        }
        internal void OnKill(EnemyState enemy)
        {
            foreach (var group in State.Groups)
            {
                if (group.Health <= 0 || !group.Participants.Contains(enemy.Id)) { continue; }
                if (State.Completed.Add("training:" + group.Id + ":" + enemy.Id)) { group.Training++; State.Completed.Add("mission-kill:" + group.Id + ":" + group.Mission); Emit("group-trained", group.Source, group.Id, group.Position, 1); }
            }
        }
        private void TraceWorkPath()
        {
            if (!modules.Has(Definition.ChapterId, "unit:map-route-trace") || State.Paths.Count >= Definition.PathCapacity) { return; }
            var nearWork = false;
            foreach (var work in State.Work)
            { if (work.Health > 0 && Near(work.Position, world.Lord, Gear(work.Source).WorkRadius)) { nearWork = true; break; } }
            if (!nearWork) { return; }
            foreach (var path in State.Paths) { if (Near(path, world.Lord, Definition.PathSpacing)) { return; } }
            State.Paths.Add(world.Lord); Emit("work-path", "", -1, world.Lord);
        }
    }
}
