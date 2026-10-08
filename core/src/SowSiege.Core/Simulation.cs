namespace SowSiege.Core;

/// <summary>Seeded fixed-tick world. IO, clocks, rendering and meta progression belong to adapters.</summary>
public sealed class Simulation
{
    private readonly ContentCatalog catalog;
    private readonly RunOptions options;
    private readonly TrackedRandom random;
    private readonly SpatialHash spatial;
    private readonly CombatSystem combat;
    private readonly EstateSystem estate;
    private readonly ProgressionSystem progression;
    internal RuntimeSystem? Runtime { get; }
    internal ExperimentSystem? Experiment { get; }
    internal WorldState World { get; } = new();
    private readonly string peopleRule;

    public Simulation(ContentCatalog catalog, RunOptions options)
    {
        this.catalog = catalog;
        this.options = options;
        peopleRule = options.PeopleRule ?? catalog.Tuning.World.DefaultPeopleRule;
        if (peopleRule is not ("A" or "B" or "C")) { throw new ArgumentException("Unknown people rule."); }
        if (options.Scenario is not ("normal" or "load")) { throw new ArgumentException("Unknown scenario."); }
        random = new(options.Seed);
        spatial = new(catalog.Tuning.World.Map.CellSize);
        var map = catalog.Tuning.World.Map;
        World.Estate = new(map.Width >> 1, map.Height >> 1);
        World.Lord = World.Estate;
        World.Destination = World.Lord;
        World.LordHealth = map.LordHealth;
        World.Food = catalog.Tuning.World.People.InitialFood;
        foreach (var id in catalog.Tools.Keys.Order(StringComparer.Ordinal)) { World.Tools.Add(id, new()); }
        World.Equipment.Add(new() { Id = catalog.Heroes[options.HeroId].StartingTool });
        World.Equipment.Add(new() { Id = catalog.Tuning.World.Progression.StartingWeapon });
        if (catalog.Experiment is not null) { Experiment = new(catalog, options, World); }
        else if (options.Movement is not null) { throw new ArgumentException("Movement requires an experiment profile."); }
        if (catalog.Runtime is not null) { Runtime = new(catalog, World, random, spatial); }
        combat = new(catalog, options, World, random, spatial, Runtime, Experiment);
        estate = new(catalog, options, World, peopleRule, spatial, Runtime, Experiment);
        progression = new(catalog, options, World, random, Runtime, Experiment);
        World.Rerolls = catalog.Tuning.World.Progression.Rerolls;
        World.Bans = catalog.Tuning.World.Progression.Bans;
        World.Locks = catalog.Tuning.World.Progression.Locks;
        estate.Initialize();
        if (Runtime is not null) { Runtime.HarvestFarm = estate.Harvest; Runtime.PlantFarm = estate.Plant; }
        if (options.Scenario == "load") { PrepareLoadTick(); }
        Experiment?.Trace();
        if (Runtime is not null || Experiment is not null) { RecordSample(); }
    }

    public CardOfferSnapshot PendingCards => new(World.PendingCards.ToArray(), World.LockedCard, World.Rerolls, World.Bans, World.Locks);
    public void RerollCards() => progression.Reroll();
    public void BanCard(string id) => progression.Ban(id);
    public void LockCard(string id) => progression.Lock(id);
    public void ChooseCard(string id) => progression.Select(id);

    public bool IsComplete => World.Tick >= catalog.Tuning.DurationTicks || World.LordHealth <= 0;
    public WorldSnapshot Snapshot => new(World.Tick, World.Season, World.Lord.X, World.Lord.Y, World.LordHealth,
        World.Enemies.Count(enemy => enemy.Health > 0), World.Farms.Count, World.Buildings.Count(building => building.Built && building.Health > 0),
        World.People.Count, World.Farms.Count(farm => farm.Stage == 0), World.Farms.Count(farm => farm.Stage == catalog.Tuning.World.Farms.StageTicks.Length - 1),
        World.Buildings.Count(building => building.Built && building.Health <= 0), World.Food, World.People.Where(person => person.Role == "peasant").Sum(person => person.Members),
        World.People.Where(person => person.Role == "militia").Sum(person => person.Members), World.People.Where(person => person.Role == "returning").Sum(person => person.Members), World.Level, World.Harvests, World.Rebuilds, World.SpawnedEnemies, World.People.Sum(person => person.Members));

    public void PrepareLoadTick()
    {
        if (options.Scenario != "load") { throw new InvalidOperationException("Load preparation requires the explicit load scenario."); }
        combat.FillLoad();
        estate.FillLoad();
        World.LordHealth = catalog.Tuning.World.Map.LordHealth;
    }

    public void Tick()
    {
        if (IsComplete) { throw new InvalidOperationException("Run is already complete."); }
        if (World.PendingCards.Length > 0) { throw new InvalidOperationException("Choose a pending card before advancing the world."); }
        SetSeason();
        var wasInside = RuntimeSystem.Within(World.Lord, World.Estate, catalog.Tuning.World.Map.EstateRadius);
        if (Experiment is null) { combat.MoveLord(); } else { Experiment.MoveLord(); }
        if (Runtime is not null && wasInside != RuntimeSystem.Within(World.Lord, World.Estate, catalog.Tuning.World.Map.EstateRadius)) { Runtime.Emit("estate-cross", new(World.Lord)); }
        Runtime?.Tick();
        combat.SpawnAndMoveEnemies();
        spatial.Rebuild(World.Enemies);
        foreach (var equipment in World.Equipment)
        {
            if (equipment.ReadyTick > World.Tick) { continue; }
            if (catalog.Tools.TryGetValue(equipment.Id, out var tool))
            {
                combat.Activate(equipment, tool.Activation, World.Tools[tool.Id]);
                estate.ApplyGrowth(tool);
            }
            else { combat.Activate(equipment, catalog.Weapons[equipment.Id].Activation, null); }
        }
        estate.Tick();
        combat.ResolveEnemyAttacks();
        combat.ResolveDeaths();
        progression.Tick();
        if (World.Lord.DistanceSquared(World.Estate) <= (long)catalog.Tuning.World.Map.EstateRadius * catalog.Tuning.World.Map.EstateRadius) { World.EstateTicks++; }
        World.Tick++;
        Experiment?.Trace();
        if (IsComplete) { Experiment?.End(); }
        if (options.Scenario == "load") { PrepareLoadTick(); }
        if (World.Tick % catalog.Tuning.World.TelemetryPeriodTicks == 0 || IsComplete) { RecordSample(); }
    }

    private void SetSeason()
    {
        var boundary = 0;
        for (var index = 0; index < catalog.Tuning.World.Seasons.Length; index++)
        {
            boundary += catalog.Tuning.World.Seasons[index].DurationTicks;
            if (World.Tick < boundary) { World.Season = index; return; }
        }
        World.Season = catalog.Tuning.World.Seasons.Length - 1;
    }

    private void RecordSample()
    {
        Experiment?.Sample();
        var state = Snapshot;
        World.Timeline.Add(new(World.Tick, World.Season, World.Level, state.ActiveEnemies, state.Farms, state.Buildings, state.People,
            World.WeaponDamage, World.Tools.Values.Sum(tool => tool.ActivationDamage), World.Tools.Values.Sum(tool => tool.GrowthDamage),
            World.KillExperience, World.HarvestExperience, World.TaxExperience, World.Food, World.LordHealth, World.AllyDamage));
    }

    public SimulationResult Result()
    {
        var hash = StateHash.Compute(new { options, catalog, World, RandomDraws = random.Draws });
        var tools = World.Tools.ToDictionary(pair => pair.Key, pair => new ToolTelemetry(pair.Value.ActivationDamage, pair.Value.GrowthProduced, pair.Value.GrowthDamage, pair.Value.Activations), StringComparer.Ordinal);
        return new(Runtime is null ? "S2 deterministic headless world; provisional gameplay tuning" : "S4 bounded runtime projections; provisional gameplay tuning", options.Seed, options.HeroId, options.EstateId,
            options.Policy, catalog.Heroes[options.HeroId].StartingTool, World.Tick, World.WeaponDamage + World.AllyDamage + tools.Values.Sum(tool => tool.ActivationDamage + tool.GrowthDamage),
            tools.Values.Sum(tool => tool.GrowthProduced), hash, peopleRule, options.Scenario, World.LordHealth <= 0 ? "death" : IsComplete ? "duration" : "running", World.LordHealth > 0,
            World.Level, World.Season, random.Draws, World.WeaponDamage, tools, World.Timeline.ToArray(), World.KillExperience, World.HarvestExperience, World.TaxExperience,
            World.Food, World.People.Where(person => person.Role == "peasant").Sum(person => person.Members), World.People.Where(person => person.Role == "militia").Sum(person => person.Members), World.People.Count(person => person.Role == "vassal"),
            World.Harvests, World.Ruins, World.Rebuilds, World.EstateTicks, World.SpawnedEnemies, World.DeathCause, World.Cards.ToArray(), World.AllyDamage, Runtime?.Result(), Experiment?.Result());
    }
}
