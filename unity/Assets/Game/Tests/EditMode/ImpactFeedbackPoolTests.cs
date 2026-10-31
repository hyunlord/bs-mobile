using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class ImpactFeedbackPoolTests
    {
        [Test]
        public void HitEmitsThreeToFiveShardsAndExpiresWithoutLeavingParticles()
        {
            var pool = new ImpactFeedbackPool();
            for (var id = 0; id < 30; id++)
            {
                pool.Clear(); pool.Hit(id, Vector2.one);
                Assert.That(pool.Count, Is.InRange(3, 5));
                for (var i = 0; i < pool.Count; i++)
                {
                    Assert.That(pool[i].Kind, Is.EqualTo(ImpactParticleKind.Shard));
                    Assert.That(pool[i].Origin, Is.EqualTo(Vector2.one));
                }
                pool.Advance(.2f);
                Assert.That(pool.Count, Is.Zero);
            }
        }

        [Test]
        public void DeathAddsDustButOnlyAuthoritativeExperienceAddsGem()
        {
            var pool = new ImpactFeedbackPool();
            pool.Kill(5, Vector2.zero, false);
            Assert.That(pool[pool.Count - 1].Kind, Is.EqualTo(ImpactParticleKind.Dust));
            pool.Clear(); pool.Kill(5, Vector2.zero, true);
            Assert.That(pool[pool.Count - 1].Kind, Is.EqualTo(ImpactParticleKind.Experience));
            pool.Advance(.1f);
            Assert.That(pool[pool.Count - 1].Position.y, Is.GreaterThan(0));
        }

        [Test]
        public void DenseEventsKeepFixedStorageAndHotLoopDoesNotAllocate()
        {
            var pool = new ImpactFeedbackPool();
            for (var i = 0; i < 1000; i++) pool.Kill(i, Vector2.zero, true);
            Assert.That(pool.Count, Is.EqualTo(ImpactFeedbackPool.Capacity));
            Assert.That(pool.Dropped, Is.GreaterThan(0));
            pool.Advance(1);
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) { pool.Hit(i, Vector2.zero); pool.Advance(.1f); }
            var after = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.Zero);
        }
    }
}
