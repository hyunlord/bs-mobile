using System.Collections.Generic;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class RunCoordinatorSnapshotTests
    {
        [Test] public void PublicSnapshotsRetainEnemyValuesAcrossRepeatedBorrowedStorageReuse()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var template = session.View.CaptureFrame();
            var waveTemplate = ((IWaveRunView)session.View).CaptureWaveRuntime();
            Assert.That(waveTemplate, Is.Not.Null);
            var enemies = new List<EnemyView> { new(1, "test:enemy", new(10, 20), 30, 40) };
            var waveEnemies = new List<WaveEnemyView> { new(1, "approach", new(10, 20), new(30, 40), 50, false, false, 0) };
            var counters = new Dictionary<string, long> { ["enemy-killed:test:enemy"] = 1 };
            var owner = new GameObject("Snapshot retention test");
            try
            {
                var run = owner.AddComponent<RunCoordinator>();
                run.EditorSetBorrowedSnapshotsForTests(template with { Enemies = enemies.AsReadOnly() },
                    waveTemplate with { Enemies = waveEnemies.AsReadOnly(), Counters = counters });
                var retained = run.Frame; var retainedWave = run.Wave;
                var expected = enemies[0]; var expectedWave = waveEnemies[0];
                Assert.That(run.Frame, Is.SameAs(retained));
                Assert.That(run.Wave, Is.SameAs(retainedWave));
                for (var tick = 1; tick <= 4; tick++)
                {
                    enemies[0] = new(1, "test:enemy", new(10 + tick, 20), 30 - tick, 40);
                    waveEnemies[0] = new(1, "strike", new(tick, 0), new(0, tick), tick, true, false, 0);
                    counters["enemy-killed:test:enemy"] = tick + 1;
                    run.EditorSetBorrowedSnapshotsForTests(template with { Tick = tick, Enemies = enemies.AsReadOnly() },
                        waveTemplate with { Enemies = waveEnemies.AsReadOnly(), Counters = counters });
                    Assert.That(run.Frame.Enemies[0], Is.EqualTo(enemies[0]));
                    Assert.That(run.Wave.Enemies[0], Is.EqualTo(waveEnemies[0]));
                    Assert.That(retained.Enemies[0], Is.EqualTo(expected));
                    Assert.That(retainedWave.Enemies[0], Is.EqualTo(expectedWave));
                    Assert.That(run.Wave.Counters["enemy-killed:test:enemy"], Is.EqualTo(tick + 1));
                    Assert.That(retainedWave.Counters["enemy-killed:test:enemy"], Is.EqualTo(1));
                }
                enemies.Clear(); waveEnemies.Clear(); counters.Clear();
                Assert.That(retained.Enemies.Count, Is.EqualTo(1));
                Assert.That(retainedWave.Enemies.Count, Is.EqualTo(1));
                Assert.That(retainedWave.Counters["enemy-killed:test:enemy"], Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test] public void InternalCaptureDoesNotMaterializePublicCopiesWithoutConsumers()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var owner = new GameObject("Snapshot capture test");
            try
            {
                var run = owner.AddComponent<RunCoordinator>();
                for (var i = 0; i < 4; i++) run.EditorCaptureSnapshotsForTests(session, catalog);
                Assert.That(run.EditorHasDetachedSnapshotsForTests, Is.False);
                WaveRuntimeFrame accepted = null;
                run.WaveFrameAccepted += frame => accepted = frame;
                run.EditorCaptureSnapshotsForTests(session, catalog);
                Assert.That(accepted, Is.SameAs(run.Wave));
                Assert.That(run.EditorWaveExportSharesEnemyStorageForTests(accepted), Is.False);
            }
            finally { Object.DestroyImmediate(owner); }
        }

    }
}
