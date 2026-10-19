using System.Collections.Generic;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class WeaponFeelPresentationTests
    {
        const float Frame = 1f / 60;

        static DyingEnemyPose Pose(float x) => new DyingEnemyPose {
            Actor = new EnemyImpactPose { Position = new Vector2(x, 0), Dimensions = Vector2.one, Body = new ActorPose(Vector2.zero, Vector2.one, 0) },
            Tint = Color.white, UpperOpacity = 1, DefinitionId = "core:raider", Kind = WaveEnemyKind.Pursuer };

        [Test]
        public void VanishedEnemyStaysVisibleUntilItsKillThenFlashesSwellsAndFadesWithinSevenFrames()
        {
            var pool = new EnemyDeathPresentationPool();
            pool.Hold(7, Pose(0), 1);
            for (var t = 1f; t < 1 + 2 * Frame; t += Frame)
            {
                var held = pool.Sample(0, t);
                Assert.That(held.Killed, Is.False);
                Assert.That(held.ActorOpacity, Is.EqualTo(1), "Core already removed the actor; the held pose fills the event delivery gap.");
            }
            Assert.That(pool.Kill(7, 99, default, false, Vector2.right, 1 + 2 * Frame, 0), Is.EqualTo(EnemyDeathTier.Full));
            var start = 1 + 2 * Frame;
            const float knock = .05f;
            var flashes = 0; var shown = 0; var pop = 0f;
            for (var frame = 0; frame < HitReaction.DeathFrames; frame++)
            {
                var sample = pool.Sample(0, start + frame * Frame, knock);
                if (sample.Flash > 0) flashes++;
                if (sample.ActorOpacity > 0) shown++;
                Assert.That(sample.FallOpacity, Is.Zero, "No fallen body is left behind.");
                Assert.That(sample.Pop, Is.GreaterThanOrEqualTo(pop), "The body only swells while it fades.");
                pop = sample.Pop;
            }
            Assert.That(flashes, Is.EqualTo(HitReaction.FlashFrames), "White flash lasts exactly two rendered frames.");
            Assert.That(shown, Is.EqualTo(HitReaction.DeathFrames), "The burst is visible on every one of its frames.");
            Assert.That(pop, Is.GreaterThan(HitReaction.DeathPopScale * .8f));
            Assert.That(pool.Sample(0, start + HitReaction.DeathSeconds, knock).ActorOpacity, Is.Zero, "Gone after the death frames.");
            Assert.That(pool.Sample(0, start + HitReaction.DeathSeconds, knock).Position.x, Is.EqualTo(knock).Within(.0001f), "Push is the given screen-pixel knock.");
            Assert.That(pool.TakeBurst(0, start, out var eventId, out _, out _), Is.True);
            Assert.That(eventId, Is.EqualTo(99));
            Assert.That(pool.TakeBurst(0, start + Frame, out _, out _, out _), Is.False, "Death burst is emitted once.");
            pool.Advance(start + EnemyDeathPresentationPool.FullSeconds + Frame);
            Assert.That(pool.Count, Is.Zero);
        }

        [Test]
        public void HitReactionValuesStayInTheUserSpecRanges()
        {
            Assert.That(HitReaction.FlashFrames, Is.EqualTo(2));
            Assert.That(HitReaction.KnockbackPixels, Is.InRange(2f, 4f));
            Assert.That(HitReaction.DeathFrames, Is.InRange(6, 8));
            Assert.That(HitReaction.BigHitStopSeconds, Is.InRange(.03f, .05f));
            Assert.That(HitReaction.BigHitStopMinIntervalSeconds, Is.GreaterThanOrEqualTo(.3f), "Evolved hit streams do not stutter the screen.");
            Assert.That(GameVisualTokens.EnemyHitFlashSeconds, Is.EqualTo(HitReaction.FlashSeconds), "One place for the flash length.");
        }

        [Test]
        public void BladeDelayHoldsTheVictimUntilTheSweepArrives()
        {
            var pool = new EnemyDeathPresentationPool();
            pool.Kill(3, 5, Pose(2), true, Vector2.up, 0, .1f);
            var waiting = pool.Sample(0, .05f);
            Assert.That(waiting.Flash, Is.Zero);
            Assert.That(waiting.ActorOpacity, Is.EqualTo(1));
            Assert.That(waiting.Position, Is.EqualTo(new Vector2(2, 0)));
            Assert.That(pool.TakeBurst(0, .05f, out _, out _, out _), Is.False);
            Assert.That(pool.Sample(0, .1f).Flash, Is.EqualTo(1));
            Assert.That(pool.TakeBurst(0, .1f, out _, out _, out _), Is.True);
        }

        [Test]
        public void ConcurrentFullSequencesAreCappedAndOverflowUsesFlashOnly()
        {
            var pool = new EnemyDeathPresentationPool();
            for (var i = 0; i < EnemyDeathPresentationPool.FullCapacity; i++)
                Assert.That(pool.Kill(i, i, Pose(i), true, Vector2.right, 0, 0), Is.EqualTo(EnemyDeathTier.Full));
            Assert.That(pool.Kill(1000, 1000, Pose(0), true, Vector2.right, 0, 0), Is.EqualTo(EnemyDeathTier.Light));
            Assert.That(pool.Overflowed, Is.EqualTo(1));
            var light = EnemyDeathPresentationPool.FullCapacity;
            Assert.That(pool.IsFull(light), Is.False);
            Assert.That(pool.Sample(light, 0).Flash, Is.EqualTo(1));
            Assert.That(pool.Sample(light, Frame).Flash, Is.EqualTo(1));
            Assert.That(pool.Sample(light, Frame).Position, Is.EqualTo(Vector2.zero), "Overflow does not push.");
            pool.Advance(2 * Frame);
            Assert.That(pool.LightCount, Is.Zero, "Overflow presentation ends after the flash; its burst is the death read.");
            Assert.That(pool.FullCount, Is.EqualTo(EnemyDeathPresentationPool.FullCapacity));
        }

        [Test]
        public void UnconfirmedAbsenceTimesOutAndReappearanceReleasesTheHold()
        {
            var pool = new EnemyDeathPresentationPool();
            pool.Hold(4, Pose(0), 0);
            pool.Release(4);
            Assert.That(pool.Contains(4), Is.False);
            pool.Hold(5, Pose(0), 0);
            pool.Advance(EnemyDeathPresentationPool.PendingSeconds);
            Assert.That(pool.Count, Is.Zero);
        }

        [Test]
        public void DeathPoolHotLoopDoesNotAllocate()
        {
            var pool = new EnemyDeathPresentationPool();
            var pose = Pose(0);
            void Cycle(float now)
            {
                for (var i = 0; i < 600; i++) pool.Kill(i, i, pose, true, Vector2.right, now, i * .0002f);
                for (var t = 0; t < 40; t++)
                {
                    for (var i = 0; i < pool.Count; i++) { pool.TakeBurst(i, now + t * Frame, out _, out _, out _); pool.Sample(i, now + t * Frame); }
                    pool.Advance(now + t * Frame);
                }
            }
            Cycle(0);
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            Cycle(10);
            Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }

        [Test]
        public void BladeEnvelopeEnclosesEveryOnScreenKillAndStaysOnScreenWithoutKills()
        {
            var view = new Rect(-2, -4, 4, 8);
            var origin = Vector2.zero;
            GiantBladeEnvelope.Fit(origin, -90, 6, view, new Vector2[0], 0, out var radius, out var half);
            Assert.That(radius, Is.EqualTo(2 / Mathf.Cos(45 * Mathf.Deg2Rad) * GiantBladeEnvelope.ViewportFill).Within(.001f),
                "Core reach 6 is clamped to the shortest visible distance across the fan, here its side edges.");
            Assert.That(GiantBladeEnvelope.FullyVisible(origin, -90, radius, half, view), Is.True, "Without kills the whole fan stays on screen.");
            Assert.That(half, Is.EqualTo(GiantBladeEnvelope.CoreHalfAngle));
            var kills = new[] { new Vector2(0, -3.9f), new Vector2(1.9f, -1.2f), new Vector2(-1.5f, .4f), new Vector2(.2f, -.5f) };
            GiantBladeEnvelope.Fit(origin, -90, 6, view, kills, kills.Length, out radius, out half);
            foreach (var kill in kills)
                Assert.That(GiantBladeEnvelope.Contains(origin, -90, radius, half, kill), Is.True, kill.ToString());
            Assert.That(GiantBladeEnvelope.Opacity(GiantBladeEnvelope.HoldSeconds - .001f), Is.EqualTo(1), "Trajectory holds full strength at least 0.15s.");
            Assert.That(GiantBladeEnvelope.Opacity(GiantBladeEnvelope.LifetimeSeconds), Is.Zero);
            Assert.That(GiantBladeEnvelope.ArrivalSeconds(-90, origin, new Vector2(-1, -1)), Is.LessThan(GiantBladeEnvelope.ArrivalSeconds(-90, origin, new Vector2(1, -1))),
                "Victims fall in sweep order.");
        }

        [Test]
        public void HeroHitFlashesTwoFramesPushesBrieflyAndContactDamageCannotHoldIt()
        {
            var hit = new HeroHitFeedback();
            Assert.That(hit.Trigger(0, Vector2.left), Is.True);
            Assert.That(hit.NextFlash(), Is.EqualTo(1));
            Assert.That(hit.NextFlash(), Is.EqualTo(1));
            Assert.That(hit.NextFlash(), Is.Zero);
            var peak = hit.Offset(HeroHitFeedback.PushOutSeconds, 1);
            Assert.That(peak.x, Is.EqualTo(-HeroHitFeedback.PushBodyFraction).Within(.0001f));
            Assert.That(HeroHitFeedback.PushBodyFraction, Is.InRange(.2f, .3f));
            Assert.That(hit.Offset(HeroHitFeedback.PushSeconds, 1), Is.EqualTo(Vector2.zero));
            Assert.That(hit.Trigger(.1f, Vector2.left), Is.False, "Repeated contact inside the retrigger window adds no flash.");
            Assert.That(hit.NextFlash(), Is.Zero);
            var flashed = 0;
            for (var t = 0f; t < 1; t += Frame) { hit.Trigger(t, Vector2.left); flashed += hit.NextFlash() > 0 ? 1 : 0; }
            Assert.That(flashed, Is.LessThanOrEqualTo(2 * Mathf.CeilToInt(1 / HeroHitFeedback.RetriggerSeconds)), "Continuous contact never becomes a held colour.");
        }

        static WaveGearDefinition Gear(int count, int range, WaveGearLevel[] levels = null) =>
            new WaveGearDefinition("core:muster_horn", "test", WaveAttackKind.MusterWave, 8, range, 100, 90, count, 90, 40, 90, 1000, 15, 8, levels);

        static WaveGearLevel[] CanonicalLevels()
        {
            var ranges = new[] { 1100, 1100, 1135, 1135, 1170, 1170, 1205, 1205, 1240, 1240, 1275, 1275 };
            var levels = new WaveGearLevel[ranges.Length];
            for (var i = 0; i < ranges.Length; i++) levels[i] = new WaveGearLevel(8, ranges[i], 100, 90, 1, 90, 40);
            return levels;
        }

        [Test]
        public void CanonicalMaxLevelMusterKeepsItsBaselineDrawing()
        {
            var canonical = Gear(1, 1100, CanonicalLevels());
            for (var level = 1; level <= 12; level++)
            {
                var ratio = MusterFormationRule.Ratio(canonical, level, canonical);
                Assert.That(ratio, Is.EqualTo(1));
                var formation = MusterFormationRule.Resolve(1, ratio, null);
                Assert.That(formation.Path, Is.EqualTo(MusterPresentationPath.None), "Level " + level);
                Assert.That(formation.Soldiers, Is.EqualTo(1));
                Assert.That(formation.Scale, Is.EqualTo(1));
            }
        }

        [Test]
        public void StrongerRuntimeGearGrowsTheFormationContinuouslyWithinCapsButNeverEnablesEvolution()
        {
            var canonical = Gear(1, 1100, CanonicalLevels());
            MusterFormation Stage(int count, int range) => MusterFormationRule.Resolve(1, MusterFormationRule.Ratio(Gear(count, range), 1, canonical), null);
            var lv1 = Stage(1, 2400); var max = Stage(8, 4000); var evolved = Stage(32, 6000);
            Assert.That(lv1.Soldiers, Is.LessThan(max.Soldiers));
            Assert.That(max.Soldiers, Is.LessThan(evolved.Soldiers));
            Assert.That(lv1.Scale, Is.LessThan(max.Scale));
            Assert.That(max.Scale, Is.LessThan(evolved.Scale));
            Assert.That(evolved.Soldiers, Is.LessThanOrEqualTo(MusterFormationRule.MaximumSoldiers));
            Assert.That(MusterFormationRule.Resolve(2, 1e6f, null).Soldiers, Is.EqualTo(5));
            Assert.That(MusterFormationRule.Resolve(1, 1e6f, null).Scale, Is.EqualTo(1.3f));
            Assert.That(MusterFormationRule.MaximumSoldiers, Is.EqualTo(5)); Assert.That(MusterFormationRule.MaximumScale, Is.EqualTo(1.3f));
            foreach (var stage in new[] { lv1, max, evolved })
            {
                Assert.That(stage.Path, Is.EqualTo(MusterPresentationPath.GearRatio));
                Assert.That(stage.Evolved, Is.False, "Gear strength alone never turns on the evolution-only look.");
            }
        }

        [Test]
        public void SoldiersAreMovedClearOfTheHeroBoxAndSpreadOverTheFrontArc()
        {
            var hero = new Rect(-.5f, 0, 1, 1.6f); var size = new Vector2(.6f, .8f); var pivot = new Vector2(.5f, .1f);
            var clearance = .45f * MusterFormationRule.HeroClearanceBodyWidths;
            foreach (var desired in new[] { hero.center, new Vector2(.2f, .3f), new Vector2(-.9f, 1.4f), new Vector2(3, 0) })
            {
                var at = MusterFormationRule.ClearOfHero(desired, size, pivot, hero, clearance, Vector2.up);
                Assert.That(MusterFormationRule.SpriteRect(at, size, pivot).Overlaps(hero), Is.False, desired.ToString());
                Assert.That((at - hero.center).magnitude, Is.GreaterThanOrEqualTo(clearance - .0001f));
            }
            Assert.That(MusterFormationRule.ClearOfHero(new Vector2(3, 0), size, pivot, hero, clearance, Vector2.up), Is.EqualTo(new Vector2(3, 0)), "Already clear soldiers stay put.");
            var first = MusterFormationRule.FrontArc(0, 5, hero, Vector2.right, 1); var last = MusterFormationRule.FrontArc(4, 5, hero, Vector2.right, 1);
            Assert.That(first.y, Is.LessThan(hero.center.y)); Assert.That(last.y, Is.GreaterThan(hero.center.y));
            Assert.That(MusterFormationRule.FrontArc(2, 5, hero, Vector2.right, 1).x, Is.GreaterThan(hero.center.x), "Arc faces the target.");
        }

        [Test]
        public void OnlyAnActiveRuntimeEvolutionEnablesTheEvolutionLook()
        {
            var requirements = new[] { new EvolutionRequirement("core:muster_horn", 3), new EvolutionRequirement("core:soup_ladle", 3) };
            var definitions = new Dictionary<string, EvolutionDefinition> {
                ["core:rally_kitchen"] = new EvolutionDefinition("core:rally_kitchen", "tool-tool", new[] { "core:muster_horn", "core:soup_ladle" }, "core:muster_horn", new RuntimeEffectDefinition[0]) };
            var inactive = new List<EvolutionClueView> { new EvolutionClueView("core:rally_kitchen", false, true, requirements, null) };
            Assert.That(MusterFormationRule.ActiveRuntimeEvolution(inactive, definitions, "core:muster_horn"), Is.Null);
            var active = new List<EvolutionClueView> { new EvolutionClueView("core:rally_kitchen", true, false, requirements, null) };
            Assert.That(MusterFormationRule.ActiveRuntimeEvolution(active, definitions, "core:muster_horn"), Is.EqualTo("core:rally_kitchen"));
            Assert.That(MusterFormationRule.ActiveRuntimeEvolution(active, definitions, "core:soup_ladle"), Is.Null, "A co-ingredient's people keep their own look.");
            Assert.That(MusterFormationRule.ActiveRuntimeEvolution(active, null, "core:muster_horn"), Is.Null);
            var formation = MusterFormationRule.Resolve(1, 1, "core:rally_kitchen");
            Assert.That(formation.Path, Is.EqualTo(MusterPresentationPath.RuntimeEvolution));
            Assert.That(formation.Soldiers, Is.EqualTo(MusterFormationRule.MaximumSoldiers));
            Assert.That(formation.Scale, Is.EqualTo(MusterFormationRule.MaximumScale));
            var wave = new Dictionary<string, WaveEvolutionDefinition> {
                ["core:test_rally"] = new WaveEvolutionDefinition("core:test_rally", "test", WaveEvolutionKind.RepairOrbit, new[] { "core:muster_horn", "core:soup_ladle" }) };
            Assert.That(MusterFormationRule.ActiveWaveEvolution(new string[0], wave, "core:muster_horn"), Is.Null);
            Assert.That(MusterFormationRule.ActiveWaveEvolution(new[] { "core:test_rally" }, wave, "core:muster_horn"), Is.EqualTo("core:test_rally"));
        }
    }
}
