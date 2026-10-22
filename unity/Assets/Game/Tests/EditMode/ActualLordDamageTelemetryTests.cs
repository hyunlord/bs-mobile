using System;
using System.IO;
using System.Collections.Generic;
using Game.App.Generated;
using Game.App;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class ActualLordDamageTelemetryTests
    {
        static void Observe(CameraScaleComparisonTelemetry visibility, int tick, float x)
        {
            visibility.BeginTick(tick, new Rect(-5, -5, 10, 10), Vector2.zero, 0, true, true);
            visibility.ObserveEnemy(7, new Rect(x - .5f, 0, 1, 1), new Vector2(x, 0), 1);
            visibility.EndTick();
        }
        static LordDamageObservation Hit(int tick, int attacker = 7, int amount = 5) =>
            new LordDamageObservation(tick, attacker, -1, "test:enemy", LordDamageKind.Strike, amount, new WorldPoint(1, 0), new WorldPoint(0, 0));
        [Test]
        public void ActualPositiveDamageHasKnownScreenEntryAndUnknownOwnerRemainsSeparate()
        {
            var visibility = new CameraScaleComparisonTelemetry(8, 8);
            var actual = new ActualLordDamageTelemetry(1, 2);
            Observe(visibility, 0, 8); Observe(visibility, 1, 1);
            actual.OnLordDamage(Hit(0)); actual.OnLordDamage(Hit(0, -1));
            actual.CompleteTick(1, visibility, true);
            var directory = Path.Combine(Path.GetTempPath(), "damage-telemetry-" + Guid.NewGuid().ToString("N"));
            try
            {
                actual.Save(directory);
                var csv = File.ReadAllLines(Path.Combine(directory, "actual-lord-damage.csv"));
                Assert.That(csv[1], Is.EqualTo("0,7,-1,test:enemy,Strike,5,1,1,1,1,1,1"));
                Assert.That(csv[2], Is.EqualTo("0,-1,-1,test:enemy,Strike,5,1,0,0,0,1,1"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        [Test]
        public void WindowAndPositiveDamageFilterDoNotInventEvents()
        {
            var actual = new ActualLordDamageTelemetry(10, 20, 1);
            actual.OnLordDamage(Hit(8)); actual.OnLordDamage(Hit(20)); actual.OnLordDamage(Hit(12, amount: 0));
            Assert.That(actual.Count, Is.Zero);
            actual.OnLordDamage(Hit(9)); actual.OnLordDamage(Hit(19));
            Assert.That(actual.Count, Is.EqualTo(1)); Assert.That(actual.Dropped, Is.EqualTo(1));
        }
        [Test]
        public void RealSessionAdvanceClassifiesDamageAgainstFollowingSnapshot()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");   // lord damage observations come from the wave enemy system
            var catalog = CanonicalContent.CreateCatalog();
            var enemies = new Dictionary<string, EnemyDefinition>();
            foreach (var pair in catalog.Enemies)
                enemies.Add(pair.Key, pair.Value with { Range = 100000, Damage = 1 });
            catalog = catalog with { Enemies = enemies };
            var session = new InteractiveSession(catalog, new InteractiveOptions(
                new RunOptions(30001, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true),
                AimMode.Movement, CanonicalContent.DataHash));
            var actual = new ActualLordDamageTelemetry(1, 600);
            var visibility = new CameraScaleComparisonTelemetry(2048, 601);
            session.LordDamageObserver = actual;
            var frame = session.View.CaptureFrame();
            for (var step = 0; step < 600 && actual.Count == 0 && session.View.Status != RunStatus.Completed; step++)
            {
                if (session.View.Status == RunStatus.AwaitingCard)
                    session.Apply(new ReplayCommand(session.NextSequence, frame.Tick, ReplayCommandKind.ChooseCard,
                        CardId: session.View.CaptureCards().Cards[0]));
                session.Apply(new ReplayCommand(session.NextSequence, frame.Tick, ReplayCommandKind.Advance));
                frame = session.View.CaptureFrame();
                visibility.BeginTick(frame.Tick, new Rect(-100000, -100000, 200000, 200000), Vector2.zero, 0, true, true);
                foreach (var enemy in frame.Enemies)
                {
                    var center = new Vector2(enemy.Position.X, enemy.Position.Y);
                    visibility.ObserveEnemy(enemy.Id, new Rect(center.x - 1, center.y - 1, 2, 2), center, 1);
                }
                visibility.EndTick();
                actual.CompleteTick(frame.Tick, visibility, true);
            }
            Assert.That(actual.Count, Is.GreaterThan(0), "The integration fixture must produce real positive HP damage.");
            var directory = Path.Combine(Path.GetTempPath(), "damage-session-" + Guid.NewGuid().ToString("N"));
            try
            {
                actual.Save(directory);
                var rows = File.ReadAllLines(Path.Combine(directory, "actual-lord-damage.csv"));
                for (var i = 1; i < rows.Length; i++)
                {
                    var cells = rows[i].Split(',');
                    Assert.That(int.Parse(cells[0]) + 1, Is.EqualTo(frame.Tick));
                    Assert.That(cells[6], Is.EqualTo("1"), "Actual Advance events must classify against T+1.");
                    Assert.That(cells[10], Is.EqualTo("1"));
                    Assert.That(int.Parse(cells[11]), Is.EqualTo(frame.Tick));
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        [Test]
        public void CallbackAndClassificationAllocateNothingAfterWarmup()
        {
            var visibility = new CameraScaleComparisonTelemetry(8, 8); Observe(visibility, 0, 8); Observe(visibility, 1, 1);
            var actual = new ActualLordDamageTelemetry(0, 10, 128);
            actual.OnLordDamage(Hit(0)); actual.CompleteTick(1, visibility, true);
            var hit = Hit(0); var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) { actual.OnLordDamage(hit); actual.CompleteTick(1, visibility, true); }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
    }
}
