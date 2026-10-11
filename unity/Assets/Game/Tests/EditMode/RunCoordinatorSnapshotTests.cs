using System.Collections.Generic;
using System.Reflection;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class RunCoordinatorSnapshotTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Test] public void PublicSnapshotsRetainEnemyValuesAcrossRepeatedBorrowedStorageReuse()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var template = session.View.CaptureFrame();
            var waveTemplate = ((IWaveRunView)session.View).CaptureWaveRuntime();
            Assert.That(waveTemplate, Is.Not.Null);
            var enemies = new List<EnemyView> { new(1, "test:enemy", new(10, 20), 30, 40) };
            var waveEnemies = new List<WaveEnemyView> { new(1, "approach", new(10, 20), new(30, 40), 50, false, false, 0) };
            var owner = new GameObject("Snapshot retention test");
            try
            {
                var run = owner.AddComponent<RunCoordinator>();
                Set(run, "CurrentFrame", template with { Enemies = enemies.AsReadOnly() });
                Set(run, "CurrentWave", waveTemplate with { Enemies = waveEnemies.AsReadOnly() });
                var retained = run.Frame; var retainedWave = run.Wave;
                var expected = enemies[0]; var expectedWave = waveEnemies[0];
                Assert.That(run.Frame, Is.SameAs(retained));
                Assert.That(run.Wave, Is.SameAs(retainedWave));
                for (var tick = 1; tick <= 4; tick++)
                {
                    enemies[0] = new(1, "test:enemy", new(10 + tick, 20), 30 - tick, 40);
                    waveEnemies[0] = new(1, "strike", new(tick, 0), new(0, tick), tick, true, false, 0);
                    Set(run, "CurrentFrame", template with { Tick = tick, Enemies = enemies.AsReadOnly() });
                    Set(run, "CurrentWave", waveTemplate with { Enemies = waveEnemies.AsReadOnly() });
                    Assert.That(run.Frame.Enemies[0], Is.EqualTo(enemies[0]));
                    Assert.That(run.Wave.Enemies[0], Is.EqualTo(waveEnemies[0]));
                    Assert.That(retained.Enemies[0], Is.EqualTo(expected));
                    Assert.That(retainedWave.Enemies[0], Is.EqualTo(expectedWave));
                }
                enemies.Clear(); waveEnemies.Clear();
                Assert.That(retained.Enemies.Count, Is.EqualTo(1));
                Assert.That(retainedWave.Enemies.Count, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test] public void InternalCaptureDoesNotMaterializePublicCopiesWithoutConsumers()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var owner = new GameObject("Snapshot capture test");
            try
            {
                var run = owner.AddComponent<RunCoordinator>();
                Set(run, "Session", session);
                typeof(RunCoordinator).GetField("runCatalog", Instance).SetValue(run, catalog);
                var capture = typeof(RunCoordinator).GetMethod("CaptureSnapshots", Instance);
                for (var i = 0; i < 4; i++) capture.Invoke(run, null);
                Assert.That(typeof(RunCoordinator).GetField("exportedFrame", Instance).GetValue(run), Is.Null);
                Assert.That(typeof(RunCoordinator).GetField("exportedWave", Instance).GetValue(run), Is.Null);
                WaveRuntimeFrame accepted = null;
                run.WaveFrameAccepted += frame => accepted = frame;
                capture.Invoke(run, null);
                Assert.That(accepted, Is.SameAs(run.Wave));
                Assert.That(accepted.Enemies, Is.Not.SameAs(((WaveRuntimeFrame)typeof(RunCoordinator).GetProperty("CurrentWave", Instance).GetValue(run)).Enemies));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        private static void Set(RunCoordinator run, string name, object value)
            => typeof(RunCoordinator).GetProperty(name, Instance).SetValue(run, value);
    }
}
