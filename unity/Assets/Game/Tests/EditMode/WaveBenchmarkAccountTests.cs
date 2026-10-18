using System;
using System.IO;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class WaveBenchmarkAccountTests
    {
        [Test] public void ForegroundSpikeBeforeLossCannotBeErasedByBoundaryExclusion()
        {
            var interval = new BenchmarkForegroundInterval(1000);
            interval.Transition(1325, false);
            interval.Transition(1825, true);
            Assert.That(interval.Finish(1840), Is.EqualTo(340));
            var steady = new BenchmarkForegroundInterval(1000);
            Assert.That(steady.Finish(1325), Is.EqualTo(325));
        }
        [Test] public void ForegroundGatePausesWithoutSpendingDebtAndResumesAfterOneBoundary()
        {
            var gate = new BenchmarkForegroundGate();
            double debt = .2; var commands = 0;
            Assert.That(gate.SkipFrame(), Is.False);
            gate.Focus(false);
            for (var i = 0; i < 5; i++)
                if (!gate.SkipFrame()) { debt -= 1d / 30; commands++; }
            Assert.That(commands, Is.Zero); Assert.That(debt, Is.EqualTo(.2));
            gate.Focus(true);
            Assert.That(gate.SkipFrame(), Is.True);
            for (var i = 0; i < 10; i++) Assert.That(gate.SkipFrame(), Is.False, "No post-resume cooldown may hide a steady-focus stall.");
        }
        [Test] public void PauseAndFocusComposeAndSameFrameTransitionsAreStillExcluded()
        {
            var gate = new BenchmarkForegroundGate();
            gate.Focus(false); gate.Pause(true); gate.Focus(true);
            Assert.That(gate.SkipFrame(), Is.True);
            gate.Pause(false); Assert.That(gate.SkipFrame(), Is.True);
            Assert.That(gate.SkipFrame(), Is.False);
            gate.Focus(false); gate.Focus(true);
            Assert.That(gate.SkipFrame(), Is.True);
            Assert.That(gate.SkipFrame(), Is.False);
            gate.Focus(true); Assert.That(gate.SkipFrame(), Is.False, "Duplicate callbacks do not create exclusions.");
        }
        [Test]
        public void OutputUsesSiblingAccountAndStableDistinctPreferenceIdentity()
        {
            var path = Path.Combine(Path.GetTempPath(), "benchmark-identity", "run");
            var first = WaveBenchmarkAccount.Parse(new[] { "--wave-benchmark", "input.ssreplay", "--wave-benchmark-output", path });
            var same = WaveBenchmarkAccount.Parse(new[] { "--wave-benchmark", "input.ssreplay", "--wave-benchmark-output", path + Path.DirectorySeparatorChar });
            var other = WaveBenchmarkAccount.Parse(new[] { "--wave-benchmark", "input.ssreplay", "--wave-benchmark-output", path + "2" });
            Assert.That(first.ProfileDirectory, Is.EqualTo(Path.GetFullPath(path) + ".account"));
            Assert.That(Path.GetDirectoryName(first.ProfileDirectory), Is.EqualTo(Path.GetDirectoryName(first.OutputDirectory)));
            Assert.That(first.PreferencesPrefix, Is.EqualTo(same.PreferencesPrefix));
            Assert.That(first.PreferencesPrefix, Is.Not.EqualTo(other.PreferencesPrefix));
            Assert.That(first.PreferencesPrefix, Does.Not.StartWith(RunPreferences.Prefix));
            Assert.That(WaveBenchmarkAccount.Parse(Array.Empty<string>()), Is.Null);
        }
        [Test]
        public void InvalidBenchmarkArgumentsNeverFallBackToPlayerProfile()
        {
            Assert.Throws<ArgumentException>(() => WaveBenchmarkAccount.Parse(new[] { "--wave-benchmark", "input" }));
            Assert.Throws<ArgumentException>(() => WaveBenchmarkAccount.Parse(new[] { "--wave-benchmark", "input", "--wave-benchmark-output", "--other" }));
            Assert.Throws<ArgumentException>(() => WaveBenchmarkAccount.Parse(new[] { "--wave-benchmark-output", "out" }));
            Assert.Throws<ArgumentException>(() => WaveBenchmarkAccount.Parse(new[] { "--crowd-target60-experiment" }));
            Assert.Throws<ArgumentException>(() => WaveBenchmarkAccount.Parse(new[] { "--wave-benchmark", "input", "--wave-benchmark-output", "/" }));
            Assert.Throws<ArgumentException>(() => WaveBenchmarkAccount.Parse(new[] { "--wave-benchmark", "input", "--wave-benchmark-output", "one", "--wave-benchmark-output", "two" }));
        }
        [Test]
        public void StartupStoresAndPreferenceMigrationStayOutsideMeasurementDirectory()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            var root = Path.Combine(Path.GetTempPath(), "benchmark-startup-" + Guid.NewGuid().ToString("N"));
            var account = WaveBenchmarkAccount.Parse(new[] { "--wave-benchmark", "fixture.ssreplay", "--wave-benchmark-output", Path.Combine(root, "measure") });
            var originalDefaults = PlayerPrefs.GetInt(RunPreferences.Prefix + "feelDefaultsVersion", -1);
            try
            {
                RunPreferences.Load(account.PreferencesPrefix);
                var catalog = CanonicalContent.CreateCatalog();
                var meta = new MetaProgression(CanonicalContent.CreateMetaCatalog(), catalog, CanonicalContent.DataHash,
                    account.ProfileDirectory, projectionPolicy: MetaProjectionPolicy.AccountOnlyWave, rewardDelivery: MetaRewardDelivery.PendingClaims);
                var ads = new RewardedAdsPolicy(new RewardedAdsAtomicStore(account.ProfileDirectory), () => 20261011);
                Assert.That(meta.State.PendingRun, Is.Null);
                Assert.That(ads.HasPendingWrite, Is.False);
                Assert.That(Directory.Exists(account.ProfileDirectory), Is.True);
                Assert.That(Directory.GetFiles(account.ProfileDirectory).Length, Is.GreaterThanOrEqualTo(2));
                Assert.That(Directory.Exists(account.OutputDirectory), Is.False, "Harness output stays empty until TryStartWaveBenchmark.");
                Assert.That(PlayerPrefs.GetInt(account.PreferencesPrefix + "feelDefaultsVersion", -1), Is.EqualTo(1));
                Assert.That(PlayerPrefs.GetInt(RunPreferences.Prefix + "feelDefaultsVersion", -1), Is.EqualTo(originalDefaults));
            }
            finally
            {
                foreach(var key in RunPreferences.Keys) PlayerPrefs.DeleteKey(account.PreferencesPrefix + key);
                PlayerPrefs.Save();
                if(Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
