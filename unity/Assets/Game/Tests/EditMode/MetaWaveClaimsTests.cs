using System;
using System.IO;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;

namespace Tests.EditMode
{
    public sealed class MetaWaveClaimsTests
    {
        private string directory;
        private long seconds;

        [SetUp]
        public void SetUp()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires the isolated wave-1a export.");
            directory = Path.Combine(Path.GetTempPath(), "sow-meta-wave-" + Guid.NewGuid().ToString("N"));
            seconds = 1000;
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void AccountOnlyWavePreservesCatalogAndHasNoMetaReplaySidecar()
        {
            var source = CanonicalContent.CreateCatalog();
            Assert.That(source.WaveRuntime, Is.Not.Null, "Run this fixture with the wave-1a export.");
            var meta = CanonicalContent.CreateMetaCatalog();
            var host = Open(source, meta);
            var started = host.BeginRun(meta.Chapters.Single(x => x.Index == 1).Id, 42);
            Assert.That(started.Catalog, Is.SameAs(source));
            Assert.That(started.ReplayContext, Is.Null);
            Assert.That(host.State.PendingRun, Is.EqualTo(started.Plan.Run));
            Assert.Throws<InvalidOperationException>(() => host.Claim(1));
        }

        [Test]
        public void IdleClaimIsDurableAndDuplicateTapDoesNotCreditAgain()
        {
            var source = CanonicalContent.CreateCatalog();
            var meta = CanonicalContent.CreateMetaCatalog();
            var host = Open(source, meta);
            seconds += meta.Economy.IdleStepSeconds;
            host.AdvanceIdle();
            var pending = host.State.PendingClaims.Single(x => x.Kind == MetaClaimKind.Idle);
            Assert.That(host.Claim(pending.Sequence).Claimed, Is.True);
            var paid = host.State;
            var reopened = Open(source, meta);
            Assert.That(reopened.Claim(pending.Sequence).Claimed, Is.False);
            Assert.That(reopened.State.Wallet, Is.EqualTo(paid.Wallet));
            Assert.That(reopened.State.ManorLevels, Is.EqualTo(paid.ManorLevels));
        }

        [Test]
        public void FailedClaimWritePreservesPendingReward()
        {
            var source = CanonicalContent.CreateCatalog();
            var meta = CanonicalContent.CreateMetaCatalog();
            var host = Open(source, meta);
            seconds += meta.Economy.IdleStepSeconds;
            host.AdvanceIdle();
            var pending = host.State.PendingClaims.Single(x => x.Kind == MetaClaimKind.Idle);
            var before = MetaSaveCodec.Encode(host.State);
            Directory.CreateDirectory(Path.Combine(directory, "progress.bin.tmp"));
            Assert.That(() => host.Claim(pending.Sequence), Throws.Exception);
            Assert.That(MetaSaveCodec.Encode(host.State), Is.EqualTo(before));
        }

        private MetaProgression Open(ContentCatalog source, MetaCatalog meta) =>
            new MetaProgression(meta, source, CanonicalContent.DataHash, directory, () => seconds, () => seconds + 10000,
                MetaProjectionPolicy.AccountOnlyWave, MetaRewardDelivery.PendingClaims);
    }
}
