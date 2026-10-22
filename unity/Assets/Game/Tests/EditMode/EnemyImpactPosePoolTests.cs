using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class EnemyImpactPosePoolTests
    {
        // The knock distance the renderer passes (HitReaction.KnockbackPixels in world units); .8 keeps these positions readable.
        const float Knock = .8f;
        static EnemyImpactPose Pose(float x, float time) => new EnemyImpactPose {
            Position = new Vector2(x, 0), AnimationTime = time, Uv = new Rect(time, 0, 1, 1),
            Facing = new ActorFacingPose(time, 0), Body = new ActorPose(Vector2.zero, Vector2.one, 0) };

        [Test]
        public void OnlyStruckEnemyHoldsPoseForFourSixtiethsAndRecoilUsesTheGivenKnock()
        {
            var pool = new EnemyImpactPosePool();
            pool.Hit(7, 1, Vector2.right);
            var first = pool.Sample(7, 1, 2, Knock, Pose(10, 1));
            Assert.That(first.Position.x, Is.EqualTo(10.8f).Within(.0001f));
            Assert.That(first.Flash, Is.True);
            Assert.That(pool.Sample(7, 1.016f, 2, Knock, Pose(12, 2)).Flash, Is.True);
            var held = pool.Sample(7, 1.05f, 2, Knock, Pose(14, 2));
            Assert.That(held.Flash, Is.False);
            Assert.That(held.Position, Is.EqualTo(first.Position));
            Assert.That(held.AnimationTime, Is.EqualTo(1));
            Assert.That(held.Uv, Is.EqualTo(first.Uv));
            Assert.That(pool.Sample(8, 1.05f, 2, Knock, Pose(20, 2)).Position.x, Is.EqualTo(20));
            var resumed = pool.Sample(7, 1.09f, 2, Knock, Pose(14, 2));
            Assert.That(resumed.Position.x, Is.GreaterThan(10.8f));
            Assert.That(resumed.AnimationTime, Is.EqualTo(2));
            Assert.That(pool.Sample(7, 1.3f, 2, Knock, Pose(15, 3)).Position.x, Is.EqualTo(15));
        }

        [Test]
        public void FirstRenderedSampleStartsFullHoldAfterEventDelivery()
        {
            var pool = new EnemyImpactPosePool(); pool.Hit(7, 0, Vector2.right);
            pool.Sample(7, .02f, 1, Knock, Pose(0, 0));
            Assert.That(pool.Sample(7, .08f, 1, Knock, Pose(2, 2)).AnimationTime, Is.Zero);
            Assert.That(pool.Sample(7, .1f, 1, Knock, Pose(2, 2)).AnimationTime, Is.EqualTo(2));
        }

        [Test]
        public void OrdinaryBodyColorsAreNotTheRedDangerSignal()
        {
            foreach (SowSiege.Core.WaveEnemyKind kind in System.Enum.GetValues(typeof(SowSiege.Core.WaveEnemyKind)))
                Assert.That(WorldRenderer.EnemyBodyTint(kind), Is.Not.EqualTo(GameVisualTokens.Hostile));
        }

        [Test]
        public void RepeatedHitsDoNotExtendOneContinuousPoseHold()
        {
            var pool = new EnemyImpactPosePool(); pool.Hit(7, 0, Vector2.right);
            pool.Sample(7, 0, 1, Knock, Pose(0, 0)); pool.Hit(7, .05f, Vector2.right);
            Assert.That(pool.Sample(7, .09f, 1, Knock, Pose(1, 1)).AnimationTime, Is.EqualTo(1));
            pool.Clear(); Assert.That(pool.Sample(7, .1f, 1, Knock, Pose(1, 1)).Position.x, Is.EqualTo(1));
        }

        [Test]
        public void RehitDuringRecoveryRefreshesFlashWithoutRestartingHeldPose()
        {
            var pool = new EnemyImpactPosePool(); pool.Hit(7, 0, Vector2.right);
            pool.Sample(7, 0, 1, Knock, Pose(0, 0));
            Assert.That(pool.Sample(7, .09f, 1, Knock, Pose(1, 1)).Flash, Is.False);
            pool.Hit(7, .1f, Vector2.left);
            var rehit = pool.Sample(7, .1f, 1, Knock, Pose(1, 1));
            Assert.That(rehit.Flash, Is.True);
            Assert.That(rehit.AnimationTime, Is.EqualTo(1), "A new flash must not restart the held animation.");
            Assert.That(pool.Sample(7, .12f, 1, Knock, Pose(1, 2)).Flash, Is.True);
            Assert.That(pool.Sample(7, .14f, 1, Knock, Pose(1, 3)).Flash, Is.False);
            pool.Hit(7, .17f, Vector2.right);
            pool.Advance(.19f);
            Assert.That(pool.Sample(7, .19f, 1, Knock, Pose(2, 4)).Flash, Is.True,
                "A flash crossing the original recoil expiry must survive cleanup.");
            Assert.That(pool.Sample(7, .19f, 1, Knock, Pose(2, 4)).Position.x, Is.EqualTo(2));
            pool.Advance(.21f);
            Assert.That(pool.Count, Is.Zero);
        }

        [Test]
        public void FixedCapacitySaturatesWithoutAllocatingAndExpiredSlotsAreReused()
        {
            var pool = new EnemyImpactPosePool(); var pose = Pose(1, 0);
            pool.Hit(0, 0, Vector2.up); pool.Sample(0, 0, 1, Knock, pose); pool.Clear();
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < EnemyImpactPosePool.Capacity + 1; i++) { pool.Hit(i, 0, Vector2.up); pool.Sample(i, 0, 1, Knock, pose); }
            pool.Advance(1);
            for (var i = 0; i < EnemyImpactPosePool.Capacity; i++) pool.Hit(i, 1, Vector2.up);
            Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
            Assert.That(pool.Count, Is.EqualTo(EnemyImpactPosePool.Capacity));
            Assert.That(pool.Dropped, Is.EqualTo(1));
        }
    }
}
