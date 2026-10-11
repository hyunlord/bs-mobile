using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class HostRewardReplayTests
{
    private static readonly string Hash = new('A', 64);
    private static ContentCatalog Catalog() => ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
    private static InteractiveOptions Options(ContentCatalog c, HostRewardRunOptions? rewards = null) =>
        new(new(30001, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "manual", ManualCards: true), AimMode.Movement, Hash, HostRewards: rewards);
    private static ReplayCommand Command(InteractiveSession s, ReplayCommandKind kind, int value = 0) =>
        new(s.NextSequence, s.View.CaptureFrame().Tick, kind, Value: value);

    [Fact]
    public void RerollGrantIsSeparateFromSpendAndLimitedToTwoRecordedGrants()
    {
        var c = Catalog(); var s = new InteractiveSession(c, Options(c, new(true, null)));
        var commands = new List<ReplayCommand>();
        void Apply(ReplayCommandKind kind, int value = 0) { var command = Command(s, kind, value); s.Apply(command); commands.Add(command); }
        Assert.Throws<InvalidOperationException>(() => s.Apply(Command(s, ReplayCommandKind.HostGrantRerollV1, 1)));
        Apply(ReplayCommandKind.GrantLevel);
        while (s.View.CaptureCards().Rerolls > 0)
        {
            Apply(ReplayCommandKind.RerollCards);
        }

        for (int i = 0; i < 2; i++)
        {
            string[] offer = s.View.CaptureCards().Cards.ToArray();
            Apply(ReplayCommandKind.HostGrantRerollV1, 1);
            Assert.Equal(offer, s.View.CaptureCards().Cards);
            Assert.Equal(1, s.View.CaptureCards().Rerolls);
            Assert.Equal(i + 1, s.HostRerollsGranted);
            Assert.Throws<InvalidOperationException>(() => s.Apply(Command(s, ReplayCommandKind.HostGrantRerollV1, 1)));
            Apply(ReplayCommandKind.RerollCards);
        }
        Assert.Throws<InvalidOperationException>(() => s.Apply(Command(s, ReplayCommandKind.HostGrantRerollV1, 1)));
        var replay = Roundtrip(s, commands);
        Assert.Equal(5, replay.Header.FormatVersion);
        Assert.Equal(s.ComputeStateHash(), ReplayRunner.Verify(c, Hash, replay).StateHash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RevivalRequiresDeathPreservesWorldAndExpiresImmunity(bool legacy)
    {
        var c = legacy ? InteractiveTests.Catalog() : Catalog();
        var s = new InteractiveSession(c, Options(c, new(false, new(10, 2, HostRevivePreservation.PreserveWorldAndCards))));
        Assert.False(s.CanHostRevive);
        Assert.Throws<InvalidOperationException>(() => s.Apply(Command(s, ReplayCommandKind.HostReviveV1)));
        s.Simulation.World.LordHealth = 0;
        var position = s.Simulation.World.Lord;
        var enemy = new EnemyState { Id = 999, Definition = c.Enemies.Keys.First(), Position = position, Health = 100 };
        s.Simulation.World.Enemies.Add(enemy);
        int count = s.Simulation.World.Enemies.Count;
        Assert.True(s.CanHostRevive);
        s.Apply(Command(s, ReplayCommandKind.HostReviveV1));
        Assert.Equal(10, s.Simulation.World.LordHealth);
        Assert.Equal(position, s.Simulation.World.Lord);
        Assert.Equal(count, s.Simulation.World.Enemies.Count);
        Assert.True(s.GetSummary().Revived);
        Assert.True(s.HostRevived);
        Assert.Equal(2, s.State.HostRewards!.InvulnerableUntil);
        s.Apply(Command(s, ReplayCommandKind.Advance));
        Assert.Equal(10, s.Simulation.World.LordHealth);
        Assert.False(s.State.Invulnerable);
        s.Simulation.World.LordHealth = 0;
        Assert.False(s.CanHostRevive);
        Assert.Throws<InvalidOperationException>(() => s.Apply(Command(s, ReplayCommandKind.HostReviveV1)));
    }

    [Fact]
    public void InvalidCommandsAndOptionsFailWithoutMutation()
    {
        var c = Catalog(); var s = new InteractiveSession(c, Options(c));
        Assert.Null(s.GetSummary().Revived);
        string before = s.ComputeStateHash();
        Assert.Throws<InvalidOperationException>(() => s.Apply(Command(s, ReplayCommandKind.HostReviveV1)));
        Assert.Throws<InvalidOperationException>(() => s.Apply(Command(s, ReplayCommandKind.HostGrantRerollV1, 1)));
        Assert.Equal(before, s.ComputeStateHash());
        foreach (var command in new[] { new ReplayCommand(0, 0, ReplayCommandKind.HostGrantRerollV1), new(0, 0, ReplayCommandKind.HostGrantRerollV1, new(1, 0), Value: 1), new(0, 0, ReplayCommandKind.HostReviveV1, CardId: "x"), new(0, 0, ReplayCommandKind.HostReviveV1, Value: 1) })
        {
            Assert.Throws<ArgumentException>(() => s.Apply(command));
        }

        foreach (var rules in new[] { new HostReviveRules(0, 0, 0), new(1, -1, 0), new(1, 0, (HostRevivePreservation)9), new(int.MaxValue, 0, 0), new(1, int.MaxValue, 0) })
        {
            Assert.Throws<ArgumentException>(() => new InteractiveSession(c, Options(c, new(false, rules))));
        }

        Assert.Throws<ArgumentException>(() => new InteractiveSession(c, Options(c, new(false, null))));
        var duration = new InteractiveSession(c, Options(c, new(false, new(10, 0, 0))));
        duration.Simulation.World.LordHealth = 0; duration.Simulation.World.Tick = c.Tuning.DurationTicks;
        Assert.False(duration.CanHostRevive);
        Assert.Throws<InvalidOperationException>(() => duration.Apply(Command(duration, ReplayCommandKind.HostReviveV1)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void V5RoundtripsFeelAndFieldWithExplicitReviveRules(bool feel, bool field)
    {
        var c = Catalog(); var options = Options(c, new(true, new(50, 30, 0))) with
        { Feel = feel ? new(FasterLord: true) : null, Field = field ? new(FieldRules.Version) : null };
        var s = new InteractiveSession(c, options);
        var command = Command(s, ReplayCommandKind.Advance); s.Apply(command);
        var replay = Roundtrip(s, [command]);
        Assert.Equal(options, replay.Header.Options);
        Assert.Equal(s.ComputeStateHash(), ReplayRunner.Verify(c, Hash, replay).StateHash);
        Assert.Throws<ArgumentException>(() => ReplayCodec.WriteHeader(new MemoryStream(), replay.Header with { FormatVersion = 4 }));
        var old = ReplayCodec.Header(options with { HostRewards = null });
        using var bytes = new MemoryStream(); ReplayCodec.WriteHeader(bytes, old);
        ReplayCodec.WriteCommand(bytes, new(0, 0, ReplayCommandKind.HostGrantRerollV1, Value: 1));
        ReplayCodec.WriteEnd(bytes, new(1, 0, ReplayEndKind.Quit, s.ComputeStateHash()));
        bytes.Position = 0; Assert.Throws<InvalidDataException>(() => ReplayCodec.Read(bytes));
    }

    [Fact]
    public void ExplicitFixtureReplaysDeathRevivalAndRerollWithoutHostCallbacks()
    {
        var c = Catalog();
        string dataHash = ContentLoader.Hash(Path.Combine(AppContext.BaseDirectory, "data"), false);
        var options = Options(c, new(true, new(50, 60, HostRevivePreservation.PreserveWorldAndCards))) with
        { DataHash = dataHash, Feel = new(ReadableHealth: true) };
        var session = new InteractiveSession(c, options);
        var commands = new List<ReplayCommand>();
        void Apply(ReplayCommandKind kind, int value = 0, string? card = null)
        {
            var command = Command(session, kind, value) with { CardId = card };
            session.Apply(command); commands.Add(command);
        }
        Apply(ReplayCommandKind.GrantLevel);
        while (session.View.CaptureCards().Rerolls > 0)
        {
            Apply(ReplayCommandKind.RerollCards);
        }

        Apply(ReplayCommandKind.HostGrantRerollV1, 1);
        Apply(ReplayCommandKind.RerollCards);
        Apply(ReplayCommandKind.ChooseCard, card: session.View.CaptureCards().Cards[0]);
        Apply(ReplayCommandKind.SetSpawnPermille, 10000);
        while (session.View.Status != RunStatus.Completed)
        {
            if (session.View.Status == RunStatus.AwaitingCard)
            {
                Apply(ReplayCommandKind.ChooseCard, card: session.View.CaptureCards().Cards[0]);
            }
            else
            {
                Apply(ReplayCommandKind.Advance);
            }
        }
        Assert.True(session.CanHostRevive);
        Apply(ReplayCommandKind.HostReviveV1);
        for (int i = 0; i < 60; i++)
        {
            if (session.View.Status == RunStatus.AwaitingCard)
            {
                Apply(ReplayCommandKind.ChooseCard, card: session.View.CaptureCards().Cards[0]);
            }

            Apply(ReplayCommandKind.Advance);
            Assert.Equal(50, session.View.CaptureFrame().Lord.Health);
        }
        int immunityEnded = session.View.CaptureFrame().Tick;
        while (session.View.Status != RunStatus.Completed && session.View.CaptureFrame().Lord.Health == 50 && session.View.CaptureFrame().Tick < immunityEnded + 120)
        {
            if (session.View.Status == RunStatus.AwaitingCard)
            {
                Apply(ReplayCommandKind.ChooseCard, card: session.View.CaptureCards().Cards[0]);
            }
            else
            {
                Apply(ReplayCommandKind.Advance);
            }
        }
        Assert.True(session.View.CaptureFrame().Lord.Health < 50, "Damage must resume after fixture immunity expires.");
        Assert.True(session.GetSummary().Revived);
        var replay = Roundtrip(session, commands);
        Assert.Equal(session.ComputeStateHash(), ReplayRunner.Verify(c, dataHash, replay).StateHash);
        string? output = Environment.GetEnvironmentVariable("SOW_HOST_REPLAY_FIXTURES");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            using var bytes = File.Create(Path.Combine(output, "revive-reroll-v5.ssreplay"));
            ReplayCodec.WriteHeader(bytes, replay.Header);
            foreach (var command in replay.Commands)
            {
                ReplayCodec.WriteCommand(bytes, command);
            }

            ReplayCodec.WriteEnd(bytes, replay.End);
        }
    }

    private static ReplayDocument Roundtrip(InteractiveSession session, IReadOnlyList<ReplayCommand> commands)
    {
        using var bytes = new MemoryStream(); ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(session.Options));
        foreach (var command in commands)
        {
            ReplayCodec.WriteCommand(bytes, command);
        }

        var summary = session.GetSummary();
        ReplayCodec.WriteEnd(bytes, new(session.NextSequence, summary.Tick, summary.EndReason == "death" ? ReplayEndKind.Death : summary.EndReason == "duration" ? ReplayEndKind.Duration : ReplayEndKind.Quit, summary.StateHash));
        bytes.Position = 0; return ReplayCodec.Read(bytes);
    }
}
