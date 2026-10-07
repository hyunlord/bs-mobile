namespace SowSiege.Core;

internal sealed class EstateSystem(ContentCatalog catalog, RunOptions options, WorldState world, string rule, SpatialHash spatial)
{
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
        var amount = checked(tool.Growth.Yield * catalog.Estates[options.EstateId].GrowthMultiplier);
        for (var index = 0; index < amount; index++)
        {
            switch (tool.Growth.Target)
            {
                case "land": Plant(tool.Id); break;
                case "building": Build(tool.Id); break;
                case "people": Draft(tool.Id); break;
                default: throw new InvalidOperationException($"Unknown growth target {tool.Growth.Target}");
            }
        }
    }

    private void Plant(string source)
    {
        var tuning = catalog.Tuning.World.Farms;
        if (world.Farms.Count >= tuning.Capacity) { return; }
        if (world.Farms.Any(farm => farm.Position.DistanceSquared(world.Lord) < (long)tuning.Spacing * tuning.Spacing)) { return; }
        world.Farms.Add(new() { Id = world.AllocateId(), Position = world.Lord, Source = source });
        world.Tools[source].GrowthProduced++;
    }

    private void Build(string source)
    {
        var tuning = catalog.Tuning.World.Buildings;
        var range = catalog.Tools[source].Activation.Range;
        var building = world.Buildings.Where(building => !building.Built || building.Health < tuning.Health)
            .Where(building => building.Position.DistanceSquared(world.Lord) <= (long)range * range)
            .OrderBy(building => building.Position.DistanceSquared(world.Lord)).ThenBy(building => building.Id).FirstOrDefault();
        if (building is null) { return; }
        if (building.Built && building.Health == 0) { world.Rebuilds++; }
        building.Built = true;
        building.Health = Math.Min(tuning.Health, building.Health + tuning.RepairAmount);
        building.Source = source;
        world.Tools[source].GrowthProduced++;
    }

    private void Draft(string source)
    {
        var people = catalog.Tuning.World.People;
        var person = world.People.Where(person => person.Role == "peasant" && (rule != "B" || person.DutyUntil <= world.Tick)).OrderBy(person => person.Id).FirstOrDefault();
        if (person is null && world.People.Sum(person => person.Members) < PopulationCap()) { person = AddPerson("peasant", source); }
        if (person is null) { return; }
        person.Source = source;
        person.Role = rule == "B" ? "peasant" : "militia";
        person.DutyUntil = world.Tick + people.DraftDurationTicks;
        if (rule != "B")
        {
            var recruits = world.People.Where(candidate => candidate.Role == "peasant").OrderBy(candidate => candidate.Id)
                .Take(people.SquadSize - 1).ToArray();
            foreach (var recruit in recruits)
            {
                person.Members += recruit.Members;
                person.Health += recruit.Health;
                world.People.Remove(recruit);
            }
        }
        world.Tools[source].GrowthProduced++;
    }

    public void Tick()
    {
        TickPeople();
        TickFarms();
        TickBuildings();
        var people = catalog.Tuning.World.People;
        if (world.Tick > 0 && world.Tick % people.ConsumePeriodTicks == 0)
        {
            world.Food = Math.Max(0, world.Food - world.People.Sum(person => person.Members));
        }
        if (world.Tick > 0 && world.Tick % people.RecruitPeriodTicks == 0 && world.People.Sum(person => person.Members) < PopulationCap()) { AddPerson("peasant", ""); }
    }

    private void TickFarms()
    {
        var tuning = catalog.Tuning.World.Farms;
        var workers = world.People.Count(person => person.Role == "peasant");
        var boostedWorkers = world.People.Count(person => person.Role == "peasant" && person.DutyUntil > world.Tick);
        foreach (var farm in world.Farms)
        {
            var lastStage = tuning.StageTicks.Length - 1;
            if (farm.Stage < lastStage)
            {
                var growth = catalog.Tuning.World.Seasons[world.Season].GrowthMultiplier;
                if (growth > 0)
                {
                    farm.Progress += growth + (workers + boostedWorkers) * catalog.Tuning.World.People.WorkerGrowthBonus;
                    if (farm.Fertility > 0) { farm.Progress += tuning.FertilityGrowthBonus; farm.Fertility--; }
                    if (farm.Progress >= tuning.StageTicks[farm.Stage]) { farm.Progress = 0; farm.Stage++; }
                }
            }
            if (farm.Stage != lastStage) { continue; }
            farm.Progress++;
            if (farm.Progress < tuning.StageTicks[lastStage]) { continue; }
            var canHarvest = farm.Position.DistanceSquared(world.Lord) <= (long)tuning.HarvestRange * tuning.HarvestRange
                || world.People.Any(person => person.Role == "peasant" && person.Position.DistanceSquared(farm.Position) <= (long)tuning.HarvestRange * tuning.HarvestRange);
            if (!canHarvest) { continue; }
            farm.Stage = 0; farm.Progress = 0;
            world.Harvests++;
            world.Food = Math.Min(catalog.Tuning.World.People.FoodCapacity, world.Food + tuning.FoodPerHarvest);
            world.Experience += tuning.ExperiencePerHarvest;
            world.HarvestExperience += tuning.ExperiencePerHarvest;
        }
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
                world.Experience += tuning.TaxExperience;
            }
        }
    }

    private void TickPeople()
    {
        var tuning = catalog.Tuning.World.People;
        var returned = new List<PersonState>();
        foreach (var person in world.People)
        {
            if (person.Role == "militia" && rule == "C" && world.Tick >= person.DutyUntil) { person.Role = "returning"; }
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
            person.Position = person.Position.MoveToward(target, tuning.ReturnSpeed);
            if (person.Role == "returning" && person.Position == world.Estate)
            {
                person.Role = "peasant";
                person.Members = Math.Min(person.Members, person.Health);
                while (person.Members > 1)
                {
                    var health = Math.Min(tuning.VassalHealth, person.Health - (person.Members - 1));
                    person.Health -= health;
                    person.Members--;
                    returned.Add(new() { Id = world.AllocateId(), Position = world.Estate, Destination = world.Estate, Source = person.Source, Health = health });
                }
                person.Health = Math.Min(person.Health, tuning.VassalHealth);
            }
            if (person.Role == "returning" || world.Tick < person.AttackTick) { continue; }
            Attack(person.Position, tuning.Range, tuning.Damage * person.Members, person.Source);
            person.AttackTick = world.Tick + tuning.AttackCooldownTicks;
        }
        world.People.AddRange(returned);
    }

    private void Attack(Position position, int range, int damage, string source)
    {
        var enemy = spatial.Query(position, range).OrderBy(enemy => enemy.Position.DistanceSquared(position)).ThenBy(enemy => enemy.Id).FirstOrDefault();
        if (enemy is null) { return; }
        var dealt = Math.Min(enemy.Health, damage);
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
