using System.Numerics;

namespace SowSiege.Core;

internal sealed record RipeEpisode(int Episode, int Tick);
internal sealed class ExperimentState
{
    public int CircuitIndex;
    public bool FollowingCircuit = true;
    public List<int> MovementTrace = [];
    public List<MovementSample> Samples = [];
    public List<FarmWaitEvent> Waits = [];
    public SortedDictionary<int, RipeEpisode> OpenRipe = [];
    public SortedDictionary<int, int> Episodes = [];
    public bool Ended;
}
internal sealed class ExperimentSystem
{
    private readonly ContentCatalog catalog;
    private readonly WorldState world;
    private readonly ExperimentDefinition definition;
    private readonly ExperimentState state;
    private readonly string movement;
    public ExperimentSystem(ContentCatalog catalog, RunOptions options, WorldState world)
    {
        this.catalog = catalog; this.world = world;
        definition = catalog.Experiment ?? throw new ArgumentException("Experiment definition required.");
        movement = options.Movement ?? "circuit";
        if (movement is not ("circuit" or "harvest" or "evade")) { throw new ArgumentException("Unknown experiment movement."); }
        state = world.Experiment = new();
    }
    public void MoveLord()
    {
        var tuning = definition.Movement;
        if (movement == "circuit" || world.Tick % tuning.DecisionPeriodTicks == 0)
        {
            state.FollowingCircuit = true;
            if (movement == "harvest")
            {
                var farm = world.Farms.Where(farm => farm.Stage == catalog.Tuning.World.Farms.StageTicks.Length - 1).OrderBy(farm => farm.Position.DistanceSquared(world.Lord)).ThenBy(farm => farm.Id).FirstOrDefault();
                if (farm is not null) { world.Destination = farm.Position; state.FollowingCircuit = false; }
            }
            if (movement == "evade")
            {
                var enemy = world.Enemies.Where(enemy => enemy.Health > 0 && RuntimeSystem.Within(enemy.Position, world.Lord, tuning.EvadeRange)).OrderBy(enemy => enemy.Position.DistanceSquared(world.Lord)).ThenBy(enemy => enemy.Id).FirstOrDefault();
                if (enemy is not null)
                {
                    var dx = world.Lord.X - enemy.Position.X; var dy = world.Lord.Y - enemy.Position.Y;
                    if (dx == 0 && dy == 0) { dx = 1; }
                    var length = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    world.Destination = Clamp(new(world.Lord.X + (int)((long)dx * tuning.EvadeStep / length), world.Lord.Y + (int)((long)dy * tuning.EvadeStep / length)));
                    state.FollowingCircuit = false;
                }
            }
        }
        if (state.FollowingCircuit)
        {
            var target = Waypoint(state.CircuitIndex);
            if (world.Lord == target) { state.CircuitIndex = (state.CircuitIndex + 1) % tuning.CircuitOffsets.Length; target = Waypoint(state.CircuitIndex); }
            world.Destination = target;
        }
        world.Lord = world.Lord.MoveToward(world.Destination, catalog.Tuning.World.Map.LordSpeed);
    }
    private Position Waypoint(int index)
    {
        var offset = definition.Movement.CircuitOffsets[index]; return Clamp(new(world.Estate.X + offset.X, world.Estate.Y + offset.Y));
    }
    private Position Clamp(Position position) => new(Math.Clamp(position.X, 0, catalog.Tuning.World.Map.Width), Math.Clamp(position.Y, 0, catalog.Tuning.World.Map.Height));
    public long RequiredExperience(int level)
    {
        var n = new BigInteger(level - 1); var curve = definition.Experience;
        var required = curve.Base + curve.Linear * n + curve.Quadratic * n * n;
        return required > long.MaxValue ? long.MaxValue : (long)required;
    }
    public void Ripe(FarmState farm)
    {
        if (state.OpenRipe.ContainsKey(farm.Id)) { return; }
        var episode = state.Episodes.GetValueOrDefault(farm.Id) + 1; state.Episodes[farm.Id] = episode;
        state.OpenRipe.Add(farm.Id, new(episode, world.Tick + 1));
    }
    public void CloseRipe(FarmState farm, string outcome)
    {
        if (!state.OpenRipe.Remove(farm.Id, out var open)) { return; }
        var end = world.Tick + 1;
        state.Waits.Add(new(farm.Id, open.Episode, open.Tick, end, outcome, end - open.Tick, false));
    }
    public void Trace()
    {
        state.MovementTrace.Add(world.Lord.X); state.MovementTrace.Add(world.Lord.Y);
    }
    public void Sample() => state.Samples.Add(new(world.Tick, world.Lord.X, world.Lord.Y, world.Estate.X, world.Estate.Y, world.Lord.DistanceSquared(world.Estate)));
    public void End()
    {
        if (state.Ended) { return; }
        state.Ended = true;
        foreach (var pair in state.OpenRipe)
        {
            state.Waits.Add(new(pair.Key, pair.Value.Episode, pair.Value.Tick, world.Tick, world.LordHealth <= 0 ? "death-censored" : "duration-censored", world.Tick - pair.Value.Tick, true));
        }
        state.OpenRipe.Clear();
    }
    public ExperimentResult Result() => new(movement, world.LordHealth <= 0 ? world.Tick : null, state.Samples.ToArray(), state.Waits.ToArray(), state.MovementTrace.ToArray());
}
