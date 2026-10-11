using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class ActorMotionTests
    {
        [Test]
        public void HeroPoseDoesNotHopTiltOrSquashInAnyAction()
        {
            for (var sample = 0; sample < 120; sample++)
            {
                var pose = ActorMotion.Sample(sample / 60f, true, sample / 120f, sample / 120f);
                Assert.That(pose.Offset, Is.EqualTo(Vector2.zero));
                Assert.That(pose.Scale, Is.EqualTo(Vector2.one));
                Assert.That(pose.Degrees, Is.Zero);
            }
        }

        [Test]
        public void TurningNeverNarrowsOrTiltsTheSilhouette()
        {
            var motion = new ActorFacingMotion(Vector2.right, 0);
            motion.SetDirection(Vector2.left, 1);
            for (var sample = 0; sample <= 20; sample++)
            {
                var pose = motion.Sample(1 + sample * .01f);
                Assert.That(Mathf.Abs(pose.SignedWidth), Is.EqualTo(1));
                Assert.That(pose.LeanDegrees, Is.Zero);
            }
        }

        [TestCase(WaveEnemyKind.Pursuer)]
        [TestCase(WaveEnemyKind.Shield)]
        [TestCase(WaveEnemyKind.FloodBoss)]
        public void ArmoredAndHeavyActorsStayGrounded(WaveEnemyKind kind)
        {
            for (var sample = 0; sample < 60; sample++)
                Assert.That(ActorMotion.SampleEnemy(kind, sample / 60f, true).Offset, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void SpeciesMotionIsBoundedRigidAndDistinct()
        {
            for (var sample = 0; sample < 120; sample++)
            {
                var time = sample / 60f;
                var mite = ActorMotion.SampleEnemy(WaveEnemyKind.SeedThief, time, true);
                var grazer = ActorMotion.SampleEnemy(WaveEnemyKind.RipeGrazer, time, true);
                var wasp = ActorMotion.SampleEnemy(WaveEnemyKind.Ranged, time, true);
                var runner = ActorMotion.SampleEnemy(WaveEnemyKind.Charger, time, true);
                Assert.That(Mathf.Abs(mite.Offset.x), Is.LessThanOrEqualTo(GameVisualTokens.MiteScuttle));
                Assert.That(mite.Offset.y, Is.Zero);
                Assert.That(Mathf.Abs(grazer.Offset.y), Is.LessThanOrEqualTo(GameVisualTokens.GrazerUndulation));
                Assert.That(grazer.Offset.x, Is.Zero);
                Assert.That(wasp.Offset.y, Is.InRange(GameVisualTokens.WaspHoverHeight - GameVisualTokens.WaspHoverDrift, GameVisualTokens.WaspHoverHeight + GameVisualTokens.WaspHoverDrift));
                Assert.That(runner.Offset.y, Is.EqualTo(-GameVisualTokens.ChargerLowPosture));
                foreach (var pose in new[] { mite, grazer, wasp, runner })
                {
                    Assert.That(pose.Scale, Is.EqualTo(Vector2.one));
                    Assert.That(pose.Degrees, Is.Zero);
                }
            }
        }

        [TestCase(WaveEnemyKind.SeedThief)]
        [TestCase(WaveEnemyKind.RipeGrazer)]
        [TestCase(WaveEnemyKind.Charger)]
        public void GroundMovementStopsWhenActorStops(WaveEnemyKind kind)
            => Assert.That(ActorMotion.SampleEnemy(kind, 1.23f, false).Offset, Is.EqualTo(Vector2.zero));
    }
}
