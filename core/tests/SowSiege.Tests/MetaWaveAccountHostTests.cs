using Game.App;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class MetaWaveAccountHostTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "meta-wave-" + Guid.NewGuid().ToString("N"));
    private readonly MetaCatalog meta = MetaEngineTests.Catalog();
    private readonly ContentCatalog source = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
    private long seconds = 100;
    private MetaProgression Open() => new(meta, source, new string('A', 64), directory, () => seconds, () => seconds + 1000,
        MetaProjectionPolicy.AccountOnlyWave, MetaRewardDelivery.PendingClaims);

    [Fact]
    public void WaveAccountingNeverProjectsCatalogOrReplayContext()
    {
        var host = Open();
        var started = host.BeginRun("chapter-1", 42);
        Assert.Same(source, started.Catalog);
        Assert.Null(started.ReplayContext);
        Assert.Equal(started.Plan.Run, host.State.PendingRun);
        Assert.Equal(started.Plan.Run, MetaSaveCodec.Decode(File.ReadAllBytes(Path.Combine(directory, "progress.bin"))).State!.PendingRun);
        var options = new InteractiveOptions(new RunOptions(42, source.Tuning.DefaultHero, source.Tuning.DefaultEstate, "manual", ManualCards: true), AimMode.NearestEnemy, new string('A', 64));
        Assert.Equal(new InteractiveSession(source, options).ComputeStateHash(), new InteractiveSession(started.Catalog, options).ComputeStateHash());
        Assert.Throws<InvalidOperationException>(() => host.Claim(1));
    }

    [Fact]
    public void FailedClaimWriteDoesNotConsumeAndSuccessfulRetrySurvivesReload()
    {
        var host = Open();
        seconds += 30;
        host.AdvanceIdle();
        long sequence = Assert.Single(host.State.PendingClaims).Sequence;
        var before = MetaSaveCodec.Encode(host.State);
        string temporary = Path.Combine(directory, "progress.bin.tmp");
        Directory.CreateDirectory(temporary);
        Assert.NotNull(Record.Exception(() => host.Claim(sequence)));
        Assert.Equal(before, MetaSaveCodec.Encode(host.State));
        Directory.Delete(temporary);
        Assert.True(host.Claim(sequence).Claimed);
        Assert.Equal(3, host.State.Wallet["wood"]);
        Assert.False(Open().Claim(sequence).Claimed);
        Assert.Equal(3, Open().State.Wallet["wood"]);
    }

    [Fact]
    public void PendingChallengeDeliveryIsPassedThroughSettlementAndPriority()
    {
        var host = Open();
        host.BeginRun("chapter-1", 42);
        host.SettleRun(MetaEngineTests.Success);
        var claim = Assert.Single(host.State.PendingClaims);
        Assert.Equal(MetaClaimKind.Challenge, claim.Kind);
        host.SetManorPriority(host.State.ManorPriority);
        Assert.Single(host.State.PendingClaims);
        Assert.Empty(host.State.CompletedChallenges);
        var restored = Open();
        Assert.True(restored.Claim(claim.Sequence).Claimed);
        Assert.Contains("clear-first", restored.State.CompletedChallenges);
    }

    [Fact]
    public void SchemaThreeTerminalIntentRecoversAfterSchemaFourMigration()
    {
        var host = Open();
        var pending = host.BeginRun("chapter-1", 42).Plan.Run;
        var before = host.State;
        var after = MetaEngine.SettleRun(meta, before, pending, MetaEngineTests.Success).State;
        byte[] first = MetaSaveCodec.EncodeVersion(before, 3), second = MetaSaveCodec.EncodeVersion(after, 3);
        File.WriteAllBytes(Path.Combine(directory, "progress.bin"), first);
        using (var stream = File.Create(Path.Combine(directory, "terminal.bin")))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(first.Length); writer.Write(first); writer.Write(second.Length); writer.Write(second);
        }
        var recovered = Open();
        Assert.True(recovered.RecoveredTerminalRun);
        Assert.False(recovered.RecoveredAbandonedRun);
        Assert.Equal(after.Wallet, recovered.State.Wallet);
        Assert.Equal(1, recovered.State.CompletedRuns);
        Assert.False(Open().RecoveredTerminalRun);
    }

    [Fact]
    public void WaveFactsUseCumulativeCountersRatherThanTheRecentEventWindow()
    {
        var options = new InteractiveOptions(new RunOptions(42, source.Tuning.DefaultHero, source.Tuning.DefaultEstate, "manual", ManualCards: true), AimMode.NearestEnemy, new string('A', 64));
        var session = new InteractiveSession(source, options);
        var wave = ((IWaveRunView)session.View).CaptureWaveRuntime()! with
        {
            BossDefeated = true,
            Counters = new Dictionary<string, long> { ["enemy-killed:a"] = 7, ["enemy-killed:b"] = 3, ["harvest-complete:a"] = 4, ["reward-collected:a"] = 99 },
            Events = [],
            Work = [Work(1, "grain", false, 1), Work(2, "grain", true, 0), Work(3, "building", true, 1), Work(4, "building", false, 1)],
            Groups = [new WaveGroupView(1, "source", new(0, 0), "idle", 0, 0, 0, 0, "line")]
        };
        var facts = MetaRunAdapter.FromWave(session.GetSummary(), session.View.CaptureFrame(), wave, true);
        Assert.Equal(10, facts.Kills); Assert.Equal(4, facts.Harvests);
        Assert.Equal(1, facts.Farms); Assert.Equal(1, facts.Buildings); Assert.Equal(1, facts.People);
        Assert.True(facts.BossDefeated); Assert.True(facts.Abandoned);
    }

    private static WaveWorkView Work(int id, string kind, bool complete, int health) =>
        new(id, "source", kind, new(0, 0), 0, 1, complete, health, 0, false, false, false, false, false);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
