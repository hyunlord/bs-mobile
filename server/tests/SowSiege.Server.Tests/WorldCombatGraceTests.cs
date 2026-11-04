using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldCombatGraceTests
{
    private static readonly Lazy<WorldCombatExecutor> Shared = new(() => new(new(
        Path.Combine(ContentEstateTests.Root(), "data"), "wave-1a", 4 * 1024 * 1024, TimeSpan.FromSeconds(120), 1, PlayTimeToleranceMilliseconds: 0)));

    private static InteractiveOptions Options(WorldCombatExecutor executor)
    {
        var catalog = executor.Catalog;
        var map = catalog.Tuning.World.Map;
        var hero = catalog.Tuning.DefaultHero;
        string Hash(char c) => new(c, 64);
        var effects = Enum.GetValues<MetaAbility>().Select(ability => new WorldResolvedEffect(ability, 0, 10, 0, true, true, true,
            new[] { new WorldRawEffect(WorldEffectSourceKind.Hero, hero, Hash('A'), 0) })).ToArray();
        return executor.ExpectedOptions(new(Hash('E'), WorldCombatMode.DirectPve,
            new("wave-1a", ReviewShowcaseCatalog.Hash(catalog), "world-combat-v1", 1, Hash('C'), executor.DataHash,
                1, 1, 1, catalog.Tuning.TickRate, 30001, ReplayCodec.Header(new InteractiveOptions(new RunOptions(30001, hero, catalog.Tuning.DefaultEstate, "manual"), AimMode.Movement, executor.DataHash)).RngAlgorithm),
            new(0, 0, map.Width, map.Height), new(hero, Hash('A'), Array.Empty<WorldHeroSkill>()), new(3000, 60, 65536),
            new[] { new WorldObjective("test:hold", WorldObjectiveKind.HoldMarker, "test:marker", 0, 3, 0),
                new WorldObjective("test:time", WorldObjectiveKind.TimeLimit, "", 0, 0, 10) },
            Array.Empty<WorldTroopType>(), Array.Empty<WorldSpawnCohort>(),
            new[] { new WorldMarker("test:marker", map.Width / 2, map.Height / 2, 10, 20, true) }, effects));
    }

    [Fact]
    public async Task RepeatedSetAimModePrefixesCannotPostponeNoInputRecovery()
    {
        var executor = Shared.Value;
        var options = Options(executor);
        var frontier = new WorldInputFrontier(1000, 1000, 0, 0);
        ReplayCommand[] accepted = [];
        for (var sequence = 0; sequence < 3; sequence++)
        {
            ReplayCommand[] proposed = [.. accepted, new(sequence, 0, ReplayCommandKind.SetAimMode, Value: sequence % 2)];
            var now = 2000 + sequence * 1000;
            var validated = await executor.ValidatePrefixAsync(options, accepted, proposed, 1000, now, default);
            frontier = WorldCombatInput.Accepted(frontier, now, validated.Tick, validated.NextSequence);
            Assert.Equal(0, frontier.ConfirmedTick);
            Assert.Equal(1000, frontier.LastReceivedAtMs);
            accepted = validated.Commands;
        }
        Assert.Equal(0, WorldCombatInput.DueTick(frontier, 1000, 4000, executor.Catalog.Tuning.TickRate, 10, 3000));
        var due = WorldCombatInput.DueTick(frontier, 1000, 4001, executor.Catalog.Tuning.TickRate, 10, 3000);
        Assert.Equal(10, due);
        var recovered = await executor.RecoverAsync(options, accepted, due, default);
        Assert.NotNull(recovered.Result);
        Assert.Equal(3, recovered.Tick);
    }
}
