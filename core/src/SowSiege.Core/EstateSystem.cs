using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{

    internal sealed class EstateSystem
    {
        private readonly DiagnosticObserver? diagnostics;
        private readonly InteractiveState? interactive;
        private readonly ContentCatalog catalog;
        private readonly RunOptions options;
        private readonly WorldState world;
        private readonly string rule;
        private readonly SpatialHash spatial;
        private readonly RuntimeSystem? runtime;
        private readonly ExperimentSystem? experiment;

        public EstateSystem(ContentCatalog catalog, RunOptions options, WorldState world, string rule, SpatialHash spatial, RuntimeSystem? runtime = null, ExperimentSystem? experiment = null, DiagnosticObserver? diagnostics = null, InteractiveState? interactive = null)
        {
            this.diagnostics = diagnostics;
            this.interactive = interactive;
            this.catalog = catalog;
            this.options = options;
            this.world = world;
            this.rule = rule;
            this.spatial = spatial;
            this.runtime = runtime;
            this.experiment = experiment;
        }

        public void Initialize()
        {
            var tuning = catalog.Tuning.World;
            var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(tuning.Buildings.SiteCount)));
            for (var index = 0; index < tuning.Buildings.SiteCount; index++)
            {
                world.Buildings.Add(new()
                {
                    Id = world.AllocateId(),
                    Position = Clamp(new(world.Estate.X + (index % columns - (columns >> 1)) * tuning.Buildings.Spacing,
                    world.Estate.Y + (index / columns - (columns >> 1)) * tuning.Buildings.Spacing))
                });
            }
            AddPerson("vassal", "");
            for (var index = 0; index < tuning.People.InitialPeasants && world.People.Sum(person => person.Members) < tuning.People.MaxPeople; index++) { AddPerson("peasant", ""); }
        }

        public void ApplyGrowth(ToolDefinition tool)
        {
            if (catalog.Runtime?.Equipment.TryGetValue(tool.Id, out var projection) == true && projection.GrowthActions.Length > 0)
            {
                foreach (var action in projection.GrowthActions)
                {
                    for (var index = 0; index < action.Yield * catalog.Estates[options.EstateId].GrowthMultiplier; index++)
                    {
                        if (action.Operation == "construct") { Build(tool.Id); }
                        else if (action.Operation == "garrison") { Garrison(tool.Id, action.DurationTicks); }
                        else { throw new InvalidOperationException("Unknown mixed growth operation."); }
                    }
                }
                return;
            }
            var amount = checked(tool.Growth.Yield * catalog.Estates[options.EstateId].GrowthMultiplier);
            for (var index = 0; index < amount; index++)
            {
                switch (tool.Growth.Target)
                {
                    case "land": Plant(tool.Id, world.Lord); break;
                    case "building": Build(tool.Id); break;
                    case "people": Draft(tool.Id); break;
                    default: throw new InvalidOperationException($"Unknown growth target {tool.Growth.Target}");
                }
            }
        }

        internal bool Plant(string source, Position origin)
        {
            var tuning = catalog.Tuning.World.Farms;
            if (world.Farms.Count >= tuning.Capacity) { return false; }
            var position = runtime?.PlantingPosition(source, origin) ?? origin;
            if (world.Farms.Any(farm => farm.Position.DistanceSquared(position) < (long)tuning.Spacing * tuning.Spacing)) { return false; }
            var farm = new FarmState { Id = world.AllocateId(), Position = position, Source = source };
            world.Farms.Add(farm);
            world.Tools[source].GrowthProduced++;
            runtime?.Growth(source, "land", 1);
            runtime?.Emit("plant", new(position, source, Farm: farm));
            return true;
        }

        private void Garrison(string source, int duration)
        {
            var range = catalog.Tools[source].Activation.Range;
            var building = world.Buildings.Where(building => building.Built && building.Health > 0 && RuntimeSystem.Within(building.Position, world.Lord, range)).OrderBy(building => building.Position.DistanceSquared(world.Lord)).ThenBy(building => building.Id).FirstOrDefault();
            var person = world.People.Where(person => person.Role == "peasant" && person.Health > 0).OrderBy(person => person.Id).FirstOrDefault();
            if (building is null || person is null) { return; }
            person.Role = "guard"; person.Destination = building.Position; person.Source = source; person.DutyUntil = checked(world.Tick + duration);
            world.Tools[source].GrowthProduced++;
            runtime?.Growth(source, "people", person.Members);
        }

        private void Build(string source)
        {
            var tuning = catalog.Tuning.World.Buildings;
            var range = catalog.Tools[source].Activation.Range;
            var building = world.Buildings.Where(building => !building.Built || building.Health < tuning.Health)
                .Where(building => building.Position.DistanceSquared(world.Lord) <= (long)range * range)
                .OrderBy(building => building.Position.DistanceSquared(world.Lord)).ThenBy(building => building.Id).FirstOrDefault();
            if (building is null) { return; }
            var ruined = building.Built && building.Health == 0;
            if (world.FirstPlayable is { } fp && (!building.Built || ruined))
            {
                var definition = catalog.FirstPlayable!;
                var work = fp.BuildingWork.GetValueOrDefault(building.Id);
                if (work == 0)
                {
                    interactive?.Experience(world.Tick, PresentationKind.BuildingStarted, source, building.Position, 0);
                }

                work = Math.Min(definition.BuildingWorkRequired, work + definition.BuildingWorkPerActivation);
                fp.BuildingWork[building.Id] = work; building.Source = source;
                world.Tools[source].GrowthProduced++; runtime?.Growth(source, "building", 1);
                if (work < definition.BuildingWorkRequired)
                {
                    return;
                }

                fp.BuildingWork.Remove(building.Id);
            }
            var fresh = !building.Built;
            if (ruined) { world.Rebuilds++; }
            var context = new EffectContext(building.Position, source, Building: building, WasRuined: ruined, WasNew: fresh);
            var repair = runtime?.Modify("repair-amount", tuning.RepairAmount, context) ?? tuning.RepairAmount;
            if (runtime is not null && (fresh || ruined)) { runtime.Entity(building.Id).Facing = new(world.Lord.X - building.Position.X, world.Lord.Y - building.Position.Y); }
            building.Built = true;
            building.Health = Math.Min(tuning.Health, building.Health + repair);
            building.Source = source;
            if (fresh || ruined) { interactive?.Experience(world.Tick, PresentationKind.BuildingCompleted, source, building.Position, 0); }
            world.Tools[source].GrowthProduced++;
            runtime?.Growth(source, "building", 1);
            runtime?.Emit("repair", context);
        }

        private void Draft(string source)
        {
            var people = catalog.Tuning.World.People;
            var person = world.People.Where(person => person.Role == "peasant" && (runtime?.CanDraft(person) ?? true) && (rule != "B" || person.DutyUntil <= world.Tick)).OrderBy(person => person.Id).FirstOrDefault();
            if (person is null && world.People.Sum(person => person.Members) < PopulationCap()) { person = AddPerson("peasant", source); }
            if (person is null) { return; }
            person.Source = source;
            person.Role = rule == "B" ? "peasant" : "militia";
            person.DutyUntil = world.Tick + people.DraftDurationTicks;
            if (rule != "B")
            {
                var recruits = world.People.Where(candidate => candidate.Role == "peasant" && (runtime?.CanDraft(candidate) ?? true)).OrderBy(candidate => candidate.Id)
                    .Take(people.SquadSize - 1).ToArray();
                foreach (var recruit in recruits)
                {
                    person.Members += recruit.Members;
                    person.Health += recruit.Health;
                    world.People.Remove(recruit);
                }
            }
            world.Tools[source].GrowthProduced++;
            runtime?.Growth(source, "people", person.Members);
            diagnostics?.Draft();
            runtime?.Emit("draft", new(person.Position, source, Person: person));
        }

        public void Tick()
        {
            RemainsSystem.Absorb(world, catalog.Tuning.World.Farms.FertilityPerKill);
            TickPeople();
            TickFarms();
            TickBuildings();
            var people = catalog.Tuning.World.People;
            if (world.Tick > 0 && world.Tick % people.ConsumePeriodTicks == 0)
            {
                var consumption = world.People.Sum(person => (long)person.Members) * people.FoodPerPerson;
                world.Food = (int)Math.Max(0, world.Food - consumption);
            }
            if (world.Tick > 0 && world.Tick % people.RecruitPeriodTicks == 0 && world.People.Sum(person => person.Members) < PopulationCap()) { AddPerson("peasant", ""); }
        }

        private void TickFarms()
        {
            var tuning = catalog.Tuning.World.Farms;
            var workers = world.People.Count(person => person.Role == "peasant");
            var boostedWorkers = world.People.Count(person => person.Role == "peasant" && person.DutyUntil > world.Tick);
            diagnostics?.Labor(workers, boostedWorkers);
            foreach (var farm in world.FirstPlayable is null ? (IEnumerable<FarmState>)world.Farms : world.Farms.ToArray())
            {
                if (runtime is not null && runtime.Entity(farm.Id).PauseUntil > world.Tick) { continue; }
                var lastStage = tuning.StageTicks.Length - 1;
                if (farm.Stage < lastStage)
                {
                    var growth = catalog.Tuning.World.Seasons[world.Season].GrowthMultiplier;
                    if (growth > 0)
                    {
                        var laborBonus = (workers + boostedWorkers) * catalog.Tuning.World.People.WorkerGrowthBonus;
                        diagnostics?.LaborGrowth(laborBonus);
                        farm.Progress += growth + laborBonus;
                        if (farm.Fertility > 0) { farm.Progress += tuning.FertilityGrowthBonus; farm.Fertility--; RemainsSystem.Consumed(world, farm, tuning.FertilityGrowthBonus); }
                        if (farm.Progress >= tuning.StageTicks[farm.Stage]) { farm.Progress = 0; farm.Stage++; if (farm.Stage == lastStage) { experiment?.Ripe(farm); } }
                    }
                }
                if (farm.Stage != lastStage) { continue; }
                farm.Progress++;
                if (farm.Progress < tuning.StageTicks[lastStage]) { continue; }
                var canHarvest = farm.Position.DistanceSquared(world.Lord) <= (long)tuning.HarvestRange * tuning.HarvestRange
                    || world.People.Any(person => person.Role == "peasant" && person.Position.DistanceSquared(farm.Position) <= (long)tuning.HarvestRange * tuning.HarvestRange);
                if (!canHarvest) { continue; }
                Harvest(farm);
            }
        }

        internal void Harvest(FarmState farm)
        {
            var tuning = catalog.Tuning.World.Farms;
            experiment?.CloseRipe(farm, "harvest");
            RemainsSystem.Harvest(world, farm);
            farm.Stage = 0; farm.Progress = 0;
            world.Harvests++;
            world.Food = Math.Min(catalog.Tuning.World.People.FoodCapacity, world.Food + tuning.FoodPerHarvest);
            world.Experience += tuning.ExperiencePerHarvest;
            world.HarvestExperience += tuning.ExperiencePerHarvest;
            interactive?.Experience(world.Tick, PresentationKind.HarvestExperience, farm.Source, farm.Position, tuning.ExperiencePerHarvest);
            runtime?.Experience("land", tuning.ExperiencePerHarvest);
            runtime?.Emit("harvest", new(farm.Position, farm.Source, Farm: farm));
        }

        private void TickBuildings()
        {
            var tuning = catalog.Tuning.World.Buildings;
            foreach (var building in world.Buildings.Where(building => building.Built && building.Health > 0))
            {
                if (world.Tick >= building.AttackTick)
                {
                    Attack(building.Position, tuning.Range, tuning.Damage, building.Source);
                    building.AttackTick = world.Tick + tuning.AttackCooldownTicks;
                }
                if (world.Tick > 0 && world.Tick % tuning.TaxPeriodTicks == 0 && building.Position.DistanceSquared(world.Lord) <= (long)tuning.Range * tuning.Range)
                {
                    world.TaxExperience += tuning.TaxExperience;
                    interactive?.Experience(world.Tick, PresentationKind.TaxExperience, building.Source, building.Position, tuning.TaxExperience);
                    world.Experience += tuning.TaxExperience;
                    runtime?.Experience("building", tuning.TaxExperience);
                }
            }
        }

        private void TickPeople()
        {
            var tuning = catalog.Tuning.World.People;
            var returned = new List<PersonState>();
            foreach (var person in world.People)
            {
                if ((person.Role == "militia" && rule == "C" || person.Role == "guard") && world.Tick >= person.DutyUntil)
                {
                    person.Role = "returning";
                    runtime?.Emit("return-start", new(person.Position, person.Source, Person: person));
                }
                var target = person.Role is "vassal" or "militia" ? world.Lord : world.Estate;
                if (person.Role == "peasant")
                {
                    if (world.Tick % catalog.Tuning.World.Map.WaypointPeriodTicks == 0)
                    {
                        person.Destination = world.Estate;
                        var nearest = long.MaxValue;
                        foreach (var farm in world.Farms)
                        {
                            if (farm.Stage != catalog.Tuning.World.Farms.StageTicks.Length - 1) { continue; }
                            var distance = farm.Position.DistanceSquared(person.Position);
                            if (distance >= nearest) { continue; }
                            nearest = distance;
                            person.Destination = farm.Position;
                        }
                    }
                    target = person.Destination;
                }
                if (person.Role == "guard") { target = person.Destination; }
                var data = runtime?.Entity(person.Id);
                if (person.Role == "returning" && data?.Waypoint is Position waypoint)
                {
                    target = waypoint;
                    if (person.Position == waypoint && data.HoldUntil <= world.Tick) { data.Waypoint = null; target = world.Estate; }
                }
                var previousPosition = person.Position;
                var speed = runtime?.Modify(person.Role == "peasant" ? "worker-speed" : "draft-speed", tuning.ReturnSpeed, new(person.Position, person.Source, Person: person), 1) ?? tuning.ReturnSpeed;
                person.Position = person.Position.MoveToward(target, speed);
                if (world.FirstPlayable is { } fp)
                {
                    fp.PersonActivities[person.Id] = person.Role is "militia" or "guard" ? "muster" : person.Role == "returning" ? "return" : person.Position != previousPosition ? "move" : person.Role == "peasant" && world.Farms.Any(f => RuntimeSystem.Within(f.Position, person.Position, catalog.Tuning.World.Farms.HarvestRange)) ? "work" : "idle";
                }

                if (person.Role == "returning" && person.Position == world.Estate)
                {
                    runtime?.Emit("return-arrival", new(person.Position, person.Source, Person: person));
                    if (data is not null && data.HoldUntil > world.Tick)
                    {
                        if (world.Tick >= person.AttackTick) { Attack(person.Position, tuning.Range, tuning.Damage * person.Members, person.Source, person.Role); person.AttackTick = world.Tick + tuning.AttackCooldownTicks; }
                        continue;
                    }
                    diagnostics?.Return();
                    person.Role = "peasant";
                    runtime?.Returned(person);
                    person.Members = Math.Min(person.Members, person.Health);
                    while (person.Members > 1)
                    {
                        var health = Math.Min(tuning.VassalHealth, person.Health - (person.Members - 1));
                        person.Health -= health;
                        person.Members--;
                        var worker = new PersonState { Id = world.AllocateId(), Position = world.Estate, Destination = world.Estate, Source = person.Source, Health = health };
                        returned.Add(worker);
                        runtime?.Returned(worker);
                    }
                    person.Health = Math.Min(person.Health, tuning.VassalHealth);
                }
                if (person.Role == "returning" && (data is null || data.HoldUntil <= world.Tick) || world.Tick < person.AttackTick) { continue; }
                Attack(person.Position, tuning.Range, tuning.Damage * person.Members, person.Source, person.Role);
                person.AttackTick = world.Tick + tuning.AttackCooldownTicks;
            }
            world.People.AddRange(returned);
        }

        private void Attack(Position position, int range, int damage, string source, string? role = null)
        {
            var enemy = spatial.Query(position, range).OrderBy(enemy => enemy.Position.DistanceSquared(position)).ThenBy(enemy => enemy.Id).FirstOrDefault();
            diagnostics?.Attack(role is null ? "building" : "person", role ?? "", source, enemy is null ? 0 : 1, role is null ? catalog.Tuning.World.Buildings.AttackCooldownTicks : catalog.Tuning.World.People.AttackCooldownTicks);
            interactive?.Attack(world.Tick, source, position, new Position(1, 0), "projectile", range, enemy is null ? Array.Empty<EnemyState>() : new[] { enemy });
            if (enemy is null) { return; }
            var dealt = Math.Min(enemy.Health, damage);
            var suppressed = role is not null && diagnostics?.OffenseOff == true ? dealt : 0;
            if (suppressed > 0) { dealt = 0; }
            diagnostics?.Hit(role is null ? "building" : "person", role ?? "", source, damage, dealt, suppressed);
            enemy.Health -= dealt;
            if (world.Tools.TryGetValue(source, out var ledger)) { ledger.GrowthDamage += dealt; }
            else { world.AllyDamage += dealt; }
        }

        private int PopulationCap() => Math.Min(catalog.Tuning.World.People.MaxPeople, world.Food / catalog.Tuning.World.People.FoodPerPerson + 1);
        private PersonState AddPerson(string role, string source)
        {
            var person = new PersonState { Id = world.AllocateId(), Position = world.Estate, Destination = world.Estate, Role = role, Source = source, Health = catalog.Tuning.World.People.VassalHealth };
            world.People.Add(person);
            return person;
        }
        private Position Clamp(Position point) => new(Math.Clamp(point.X, 0, catalog.Tuning.World.Map.Width), Math.Clamp(point.Y, 0, catalog.Tuning.World.Map.Height));

        public void FillLoad()
        {
            var load = catalog.Tuning.World.Load;
            var source = catalog.Tools.Values.OrderBy(tool => tool.Id, StringComparer.Ordinal).First(tool => tool.Growth.Target == "land").Id;
            var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(load.Farms)));
            while (world.Farms.Count < load.Farms)
            {
                var index = world.Farms.Count;
                world.Farms.Add(new()
                {
                    Id = world.AllocateId(),
                    Source = source,
                    Position = Clamp(new((index % columns) * catalog.Tuning.World.Farms.Spacing,
                    (index / columns) * catalog.Tuning.World.Farms.Spacing)),
                    Stage = index % catalog.Tuning.World.Farms.StageTicks.Length
                });
            }
            var buildingSource = catalog.Tools.Values.OrderBy(tool => tool.Id, StringComparer.Ordinal).First(tool => tool.Growth.Target == "building").Id;
            foreach (var building in world.Buildings.Take(load.Buildings)) { building.Built = true; building.Health = catalog.Tuning.World.Buildings.Health; building.Source = buildingSource; }
            while (world.People.Count > load.People) { world.People.RemoveAt(world.People.Count - 1); }
            while (world.People.Count < load.People) { AddPerson("peasant", ""); }
        }
    }

}
