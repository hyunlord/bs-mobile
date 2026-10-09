using Game.App;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class MetaProgressionHostTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "meta-host-" + Guid.NewGuid().ToString("N"));
    private static string Data
    {
        get
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md")))
            {
                root = root.Parent;
            }
            return Path.Combine(root?.FullName ?? throw new InvalidOperationException("Repository missing."), "data");
        }
    }
    private readonly MetaCatalog meta = MetaContentLoader.Load(Data);
    private readonly ContentCatalog content = ContentLoader.Load(Data, profileName: "first-playable");
    private readonly string hash = new('A', 64);
    private long seconds = 1000;
    private MetaProgression Open() => new(meta, content, hash, directory, () => seconds, () => seconds + 10000);

    [Fact]
    public void PendingCrashIsAbandonedExactlyOnceOnReopen()
    {
        var host = Open();
        host.BeginRun(meta.Chapters[0].Id, 77);
        var restored = Open();
        Assert.True(restored.RecoveredAbandonedRun);
        Assert.Null(restored.State.PendingRun);
        Assert.Equal(1, restored.State.CompletedRuns);
        var again = Open();
        Assert.False(again.RecoveredAbandonedRun);
        Assert.Equal(1, again.State.CompletedRuns);
    }

    [Fact]
    public void FailedBeginAndSettlementWritesNeverPublishMemoryState()
    {
        var host = Open();
        Directory.CreateDirectory(Path.Combine(directory, "progress.bin.tmp"));
        Assert.NotNull(Record.Exception(() => host.BeginRun(meta.Chapters[0].Id, 1)));
        Assert.Null(host.State.PendingRun);
        Directory.Delete(Path.Combine(directory, "progress.bin.tmp"));
        host.BeginRun(meta.Chapters[0].Id, 1);
        Directory.CreateDirectory(Path.Combine(directory, "progress.bin.tmp"));
        Assert.NotNull(Record.Exception(() => host.AbandonRun()));
        Assert.NotNull(host.State.PendingRun);
        Assert.Equal(0, host.State.CompletedRuns);
        Directory.Delete(Path.Combine(directory, "progress.bin.tmp"));
        host.AbandonRun();
        Assert.Equal(1, host.State.CompletedRuns);
        Assert.Throws<InvalidOperationException>(() => host.AbandonRun());
    }

    [Fact]
    public void StateSnapshotsCannotMutatePersistedProgressAndPendingActionsAreBlocked()
    {
        var host = Open();
        host.State.Wallet.Clear();
        Assert.Equal(meta.Materials.Length, host.State.Wallet.Count);
        host.BeginRun(meta.Chapters[0].Id, 4);
        Assert.Throws<InvalidOperationException>(() => host.AdvanceIdle());
        Assert.Throws<InvalidOperationException>(() => host.SetManorPriority(meta.ManorBuildings[0].Id));
    }

    [Fact]
    public void IdleClockIsInjectedAndPersistsBeforeReturn()
    {
        var host = Open();
        seconds += 60;
        Assert.Equal(60, host.AdvanceIdle().CreditedSeconds);
        Assert.Equal(seconds, Open().State.LastMonotonicSeconds);
    }

    [Fact]
    public void ExactSnapshotRestoresProjectedReplayAndRejectsMismatches()
    {
        var started = Open().BeginRun(meta.Chapters[0].Id, 13);
        var options = new InteractiveOptions(new RunOptions(13, content.Tuning.DefaultHero, content.Tuning.DefaultEstate, "manual", ManualCards: true), AimMode.NearestEnemy, hash);
        var session = new InteractiveSession(started.Catalog, options);
        var commands = new List<ReplayCommand>();
        for (int i = 0; i < 30; i++)
        {
            var command = new ReplayCommand(i, i, ReplayCommandKind.Advance, new(1000, 0));
            session.Apply(command);
            commands.Add(command);
        }
        var replay = new ReplayDocument(ReplayCodec.Header(options), commands, [], new(30, 30, ReplayEndKind.Quit, session.ComputeStateHash()));
        string replayPath = Path.Combine(directory, "run.replay");
        File.WriteAllBytes(replayPath + ".meta", started.ReplayContext);
        Assert.Equal(session.ComputeStateHash(), MetaReplayFile.Verify(replayPath, Data, content, hash, replay).StateHash);
        Assert.Equal(session.ComputeStateHash(), MetaReplayContext.Verify(started.ReplayContext, content, meta, hash, replay).StateHash);
        Assert.Throws<InvalidDataException>(() => MetaReplayContext.Verify(started.ReplayContext, content, meta, new string('B', 64), replay));
        Assert.Throws<InvalidDataException>(() => ReplayRunner.Verify(content, hash, replay));
        started.ReplayContext[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => MetaReplayContext.Restore(started.ReplayContext, content, meta, hash));
    }

    [Fact]
    public void CorruptSaveRecoversAndFutureSaveRefusesFallback()
    {
        var host = Open();
        seconds += 60;
        host.AdvanceIdle();
        string path = Path.Combine(directory, "progress.bin");
        File.WriteAllBytes(path, [0]);
        Assert.Equal(AtomicSaveLoadStatus.RecoveredBackup, Open().LoadStatus);
        var bytes = File.ReadAllBytes(path);
        bytes[4] = 99;
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => Open());
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void TerminalIntentRecoversSuccessfulFactsAfterPrimaryFailureAndRestart()
    {
        var host = Open();
        host.BeginRun(meta.Chapters[0].Id, 5);
        var facts = new MetaRunFacts(content.Tuning.DurationTicks, content.Tuning.DurationTicks, true, true, false, 15, 5, 3, 8, 100, 20);
        var expected = MetaEngine.SettleRun(meta, host.State, host.State.PendingRun!, facts).State;
        string temporary = Path.Combine(directory, "progress.bin.tmp");
        Directory.CreateDirectory(temporary);
        Assert.NotNull(Record.Exception(() => host.SettleRun(facts)));
        Assert.NotNull(host.State.PendingRun);
        Assert.True(File.Exists(Path.Combine(directory, "terminal.bin")));
        Directory.Delete(temporary);
        Assert.Throws<InvalidOperationException>(() => host.AbandonRun());
        var recovered = Open();
        Assert.True(recovered.RecoveredTerminalRun);
        Assert.False(recovered.RecoveredAbandonedRun);
        Assert.Equal(expected.HighestClearedChapter, recovered.State.HighestClearedChapter);
        Assert.Equal(expected.Wallet.OrderBy(p => p.Key), recovered.State.Wallet.OrderBy(p => p.Key));
        Assert.Equal(1, recovered.State.CompletedRuns);
        var reopened = Open();
        Assert.False(reopened.RecoveredTerminalRun);
        Assert.Equal(1, reopened.State.CompletedRuns);
    }

    [Fact]
    public void StaleTerminalIntentNeverRewardsTheNextIncompleteRun()
    {
        var host = Open();
        host.BeginRun(meta.Chapters[0].Id, 1);
        host.AbandonRun();
        host.BeginRun(meta.Chapters[0].Id, 2);
        var recovered = Open();
        Assert.True(recovered.RecoveredAbandonedRun);
        Assert.False(recovered.RecoveredTerminalRun);
        Assert.Equal(2, recovered.State.CompletedRuns);
        Assert.Equal(0, recovered.State.HighestClearedChapter);
    }

    [Fact]
    public void JournalWriteFailureDoesNotCommitSettlementOrPublishCompletion()
    {
        var host = Open();
        host.BeginRun(meta.Chapters[0].Id, 1);
        Directory.CreateDirectory(Path.Combine(directory, "terminal.bin.tmp"));
        Assert.NotNull(Record.Exception(() => host.AbandonRun()));
        Assert.NotNull(host.State.PendingRun);
        Assert.Equal(0, host.State.CompletedRuns);
        Assert.False(File.Exists(Path.Combine(directory, "terminal.bin")));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
