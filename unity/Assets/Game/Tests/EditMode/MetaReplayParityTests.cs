using System;
using System.IO;
using System.Linq;
using Game.App.Generated;
using Game.Editor;
using NUnit.Framework;
using SowSiege.Core;

namespace Tests.EditMode
{
    public sealed class MetaReplayParityTests
    {
        [TestCase(52000, 1)]
        [TestCase(52001, 3)]
        [TestCase(52002, 5)]
        [TestCase(52003, 7)]
        [TestCase(52004, 10)]
        public void MonoVerifiesDotNetMetaSnapshotAndShortReplay(int seed, int chapter)
        {
            var directory = Environment.GetEnvironmentVariable("META_REPLAY_FIXTURES")
                ?? Path.Combine(FoundationBuild.RepoRoot, "artifacts/phase2a/p2/meta-replays");
            var path = Path.Combine(directory, seed + ".ssreplay");
            Assert.That(File.Exists(path), Is.True, "Generate .NET fixtures with meta-replay-fixtures before Editor tests.");
            Assert.That(File.Exists(path + ".meta"), Is.True, "Matching .NET meta snapshot is required.");
            var source = CanonicalContent.CreateCatalog();
            var meta = CanonicalContent.CreateMetaCatalog();
            var context = File.ReadAllBytes(path + ".meta");
            var projection = MetaReplayContext.Restore(context, source, meta, CanonicalContent.DataHash);
            Assert.That(projection.Plan.Run.Seed, Is.EqualTo(seed));
            Assert.That(projection.Plan.Run.ChapterId, Is.EqualTo(meta.Chapters.Single(c => c.Index == chapter).Id));
            using (var stream = File.OpenRead(path))
            {
                var replay = ReplayCodec.Read(stream);
                var result = MetaReplayContext.Verify(context, source, meta, CanonicalContent.DataHash, replay);
                Assert.That(result.Seed, Is.EqualTo(seed));
                Assert.That(result.Tick, Is.EqualTo(300));
                Assert.That(result.StateHash, Is.EqualTo(replay.End.StateHash));
                Assert.That(result.EndKind, Is.EqualTo(ReplayEndKind.Quit));
                var output = Path.Combine(FoundationBuild.RepoRoot, "artifacts/phase2a/p2/editor-meta-parity", seed + ".txt");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output, "scope=.NET-generated meta snapshot and 300-tick replay verified by Unity Editor; not device or full-run evidence\n"
                    + seed + "\t" + chapter + "\t" + result.Tick + "\t" + result.StateHash + "\t" + CanonicalContent.DataHash + "\n");
            }
        }
    }
}
