using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{

    public sealed record RemainsSample(int Tick, int Active, long Created, long Absorbed, long Expired, long Dropped, long FertilityTransferred, long FertilityConsumed, long GrowthBonusApplied, long FertilizedHarvests);
    public sealed record RemainsResult(long Created, long Absorbed, long Expired, long Dropped, int Active, long FertilityTransferred, long FertilityConsumed, long GrowthBonusApplied, long FertilizedHarvests, IReadOnlyList<RemainsSample> Samples);
    internal sealed record Remain(int Id, int EnemyId, Position Position, long CreatedTick, long ExpiresTick);
    internal sealed class RemainsState
    {
        public RemainsLoopDefinition Definition;
        public RemainsState(RemainsLoopDefinition definition) { Definition = definition; }
        public int NextId;
        public List<Remain> Live = new();
        public SortedDictionary<int, long> FarmCredits = new();
        public SortedSet<int> FertilizedCycles = new();
        public long Created;
        public long Absorbed;
        public long Expired;
        public long Dropped;
        public long FertilityTransferred;
        public long FertilityConsumed;
        public long GrowthBonusApplied;
        public long FertilizedHarvests;
        public List<RemainsSample> Samples = new();
    }
    internal static class RemainsSystem
    {
        public static void Create(WorldState world, EnemyState enemy)
        {
            var state = world.Remains;
            if (state is null) { return; }
            if (state.Live.Count >= state.Definition.Capacity) { state.Dropped++; return; }
            var created = (long)world.Tick + 1;
            state.Live.Add(new(state.NextId++, enemy.Id, enemy.Position, created, created + state.Definition.LifetimeTicks));
            state.Created++;
        }

        public static void Absorb(WorldState world, int fertility)
        {
            var state = world.Remains;
            if (state is null) { return; }
            var retained = new List<Remain>();
            foreach (var remain in state.Live)
            {
                if (world.Tick >= remain.ExpiresTick) { state.Expired++; continue; }
                var farm = world.Tick < remain.CreatedTick ? null : world.Farms
                    .Where(farm => farm.Position.DistanceSquared(remain.Position) <= (long)state.Definition.AbsorptionRadius * state.Definition.AbsorptionRadius)
                    .OrderBy(farm => farm.Position.DistanceSquared(remain.Position)).ThenBy(farm => farm.Id).FirstOrDefault();
                if (farm is null) { retained.Add(remain); continue; }
                var applied = Math.Min((long)fertility, int.MaxValue - (long)farm.Fertility);
                if (applied <= 0) { retained.Add(remain); continue; }
                farm.Fertility += (int)applied;
                state.FarmCredits.TryGetValue(farm.Id, out var credit);
                state.FarmCredits[farm.Id] = credit + applied;
                state.FertilityTransferred += applied;
                state.Absorbed++;
            }
            state.Live = retained;
        }

        public static void Consumed(WorldState world, FarmState farm, int growthBonus)
        {
            var state = world.Remains;
            if (state is null || !state.FarmCredits.TryGetValue(farm.Id, out var credit) || credit <= 0) { return; }
            state.FarmCredits[farm.Id] = credit - 1;
            state.FertilityConsumed++;
            state.GrowthBonusApplied += growthBonus;
            state.FertilizedCycles.Add(farm.Id);
        }

        public static void Harvest(WorldState world, FarmState farm)
        {
            if (world.Remains is { } state && state.FertilizedCycles.Remove(farm.Id)) { state.FertilizedHarvests++; }
        }

        public static void Destroyed(WorldState world, FarmState farm)
        {
            world.Remains?.FarmCredits.Remove(farm.Id);
            world.Remains?.FertilizedCycles.Remove(farm.Id);
        }

        public static void Sample(WorldState world)
        {
            if (world.Remains is not { } state) { return; }
            state.Samples.Add(new(world.Tick, state.Live.Count, state.Created, state.Absorbed, state.Expired, state.Dropped,
                state.FertilityTransferred, state.FertilityConsumed, state.GrowthBonusApplied, state.FertilizedHarvests));
        }

        public static RemainsResult? Result(WorldState world) => world.Remains is not { } state ? null : new(state.Created, state.Absorbed, state.Expired,
            state.Dropped, state.Live.Count, state.FertilityTransferred, state.FertilityConsumed, state.GrowthBonusApplied, state.FertilizedHarvests, state.Samples.ToArray());
    }

}
