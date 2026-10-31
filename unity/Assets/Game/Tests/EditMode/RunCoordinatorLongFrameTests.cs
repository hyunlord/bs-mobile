using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Game.Tests
{
    public sealed class RunCoordinatorLongFrameTests
    {
        sealed class ForwardedStages : IRuntimeDiagnostics
        {
            internal int Starts, Ends;
            public void Begin(RuntimeStage stage) { Starts++; }
            public void End(RuntimeStage stage) { Ends++; }
        }
        [Test]
        public void DevelopmentObserverForwardsStagesWithoutChangingReplayAndDetachesItsOwnership()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            var catalog = CanonicalContent.CreateCatalog();
            var options = new InteractiveOptions(new RunOptions(30001, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash);
            var observed = new InteractiveSession(catalog, options);
            var baseline = new InteractiveSession(catalog, options);
            var forward = new ForwardedStages(); observed.Diagnostics = forward;
            using var recorder = new DevelopmentLongFrameRecorder(startCounters: false);
            var owner = new GameObject("Long frame integration test");
            var run = owner.AddComponent<RunCoordinator>();
            try
            {
                run.EditorBeginLongFrameForTests(observed, catalog, recorder);
                var command = new ReplayCommand(0, 0, ReplayCommandKind.Advance, new PlayerInput(1000, 0));
                baseline.Apply(command);
                var sample = run.EditorApplyLongFrameForTests(command);
                Assert.That(observed.ComputeStateHash(), Is.EqualTo(baseline.ComputeStateHash()));
                Assert.That(sample.Advances, Is.EqualTo(1));
                Assert.That(sample.CoreMs, Is.GreaterThanOrEqualTo(0));
                Assert.That(sample.SnapshotMs, Is.GreaterThanOrEqualTo(0));
                Assert.That(sample.ViewMs, Is.EqualTo(-1));
                Assert.That(sample.SpriteBatchMs, Is.EqualTo(-1));
                Assert.That(sample.EffectsMs, Is.EqualTo(-1));
                if (!RuntimeDiagnosticsCapabilities.FineStagesEnabled)
                {
                    Assert.That(sample.CollectMs, Is.EqualTo(-1));
                    Assert.That(sample.HarvestMs, Is.EqualTo(-1));
                    Assert.That(sample.SpawnMs, Is.EqualTo(-1));
                    Assert.That(sample.ClearMs, Is.EqualTo(-1));
                }
                Assert.That(forward.Starts, Is.GreaterThan(0));
                Assert.That(forward.Ends, Is.EqualTo(forward.Starts));
                run.EditorDetachLongFrameForTests();
                Assert.That(observed.Diagnostics, Is.SameAs(forward));
            }
            finally { run.EditorDetachLongFrameForTests(); Object.DestroyImmediate(owner); }
        }
    }
}
