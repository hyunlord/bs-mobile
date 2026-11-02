using SowSiege.Core;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldCombatExecutorTests
{
    [Theory]
    [InlineData(WorldCombatMode.AutoPve)]
    [InlineData(WorldCombatMode.CityPvp)]
    public async Task AutoWrapperPreservesCoreResultAndMode(WorldCombatMode mode)
    {
        var fixtures = SowSiege.Sim.ArmyAutoFixtureCli.Load(Path.Combine(ContentEstateTests.Root(), "data", "opt-in", "army-auto", "hand-fixtures-v1.json"));
        var input = fixtures.Fixtures.First(x => x.Name == "victory").Input with { Mode = mode };
        var expected = ArmyAuto.Resolve(input);
        var actual = await Shared.Value.ResolveAutoAsync(input, default);
        Assert.Equal(ArmyAuto.EncodeResult(expected), ArmyAuto.EncodeResult(actual));
        Assert.Equal(mode, actual.Combat.Mode);
        Assert.Empty(actual.Combat.Objectives);
    }

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
            new("wave-1a", PortableStateCodec.HashCatalog(catalog), "world-combat-v1", 1, Hash('C'), executor.DataHash,
                1, 1, 1, catalog.Tuning.TickRate, 30001, PortableRandom.Algorithm),
            new(0, 0, map.Width, map.Height), new(hero, Hash('A'), Array.Empty<WorldHeroSkill>()), new(3000, 60, 65536),
            new[] { new WorldObjective("test:hold", WorldObjectiveKind.HoldMarker, "test:marker", 0, 3, 0),
                new WorldObjective("test:time", WorldObjectiveKind.TimeLimit, "", 0, 0, 10) },
            Array.Empty<WorldTroopType>(), Array.Empty<WorldSpawnCohort>(),
            new[] { new WorldMarker("test:marker", map.Width / 2, map.Height / 2, 10, 20, true) }, effects));
    }

    [Fact]
    public async Task RestartRecoveryMatchesUninterruptedFirstTerminal()
    {
        var executor = Shared.Value;
        var options = Options(executor);
        var first = await executor.RecoverAsync(options, [], 1, default);
        Assert.NotNull(first.Recovery);
        var restored = WorldRecoveryCodec.Restore(executor.Catalog, executor.DataHash, first.Recovery!);
        Assert.Equal(first.NextSequence, restored.NextSequence);
        var resumed = await executor.RecoverAsync(options, first.Commands, 10, default);
        var uninterrupted = await executor.RecoverAsync(options, [], 10, default);
        Assert.NotNull(resumed.Result);
        Assert.Equal(uninterrupted.Result!.StateHash, resumed.Result!.StateHash);
        Assert.Equal(3, resumed.Tick);
        Assert.Equal(uninterrupted.Commands, resumed.Commands);
        var repeated = await executor.RecoverAsync(options, resumed.Commands, 10, default);
        Assert.Equal(resumed.Result.StateHash, repeated.Result!.StateHash);
    }

    [Fact]
    public async Task FinalVerificationBindsFrozenContractAndExactAcceptedPrefix()
    {
        var executor = Shared.Value;
        var options = Options(executor);
        var completed = await executor.RecoverAsync(options, [], 10, default);
        using var output = new MemoryStream();
        ReplayCodec.WriteHeader(output, ReplayCodec.Header(options));
        foreach (var command in completed.Commands) ReplayCodec.WriteCommand(output, command);
        ReplayCodec.WriteWorldEnd(output, new(completed.NextSequence, completed.Result!));
        var verified = await executor.VerifyAsync(options, completed.Commands, output.ToArray(), 1000, 2000, default);
        Assert.Equal(completed.Result!.StateHash, verified.StateHash);
        await Assert.ThrowsAsync<InvalidDataException>(() => executor.VerifyAsync(options, [], output.ToArray(), 1000, 2000, default));
        var changed = options with { WorldCombat = new(options.WorldCombat!.Snapshot with { ContractHash = new string('F', 64) }) };
        await Assert.ThrowsAsync<InvalidDataException>(() => executor.VerifyAsync(changed, completed.Commands, output.ToArray(), 1000, 2000, default));
    }

    [Fact]
    public async Task PrefixCannotRewriteAcceptedInputOrAdvanceAheadOfUtc()
    {
        var executor = Shared.Value;
        var options = Options(executor);
        ReplayCommand[] commands = [new(0, 0, ReplayCommandKind.Advance)];
        var accepted = await executor.ValidatePrefixAsync(options, [], commands, 1000, 2000, default);
        Assert.Equal(1, accepted.Tick);
        await Assert.ThrowsAsync<InvalidDataException>(() => executor.ValidatePrefixAsync(options, commands,
            [commands[0] with { Input = new PlayerInput(1000, 0) }], 1000, 2000, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => executor.ValidatePrefixAsync(options, [], commands, 1000, 1000, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => executor.ValidatePrefixAsync(options, [],
            [new(0, 0, ReplayCommandKind.SetInvulnerable, Value: 1)], 1000, 2000, default));
    }

    [Fact]
    public async Task CancellationBeforeAdmissionDoesNotConsumeSlot()
    {
        var executor = Shared.Value;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.RecoverAsync(Options(executor), [], 1, cancelled.Token));
        var result = await executor.RecoverAsync(Options(executor), [], 1, default);
        Assert.Equal(1, result.Tick);
    }
}
