using Game.View;
using NUnit.Framework;
using SowSiege.Core;

namespace Tests.EditMode
{
    public sealed class EnemyRigAtlasTests
    {
        [TestCase(WaveEnemyKind.SeedThief)] [TestCase(WaveEnemyKind.RipeGrazer)]
        [TestCase(WaveEnemyKind.Pursuer)] [TestCase(WaveEnemyKind.Shield)]
        [TestCase(WaveEnemyKind.Charger)] [TestCase(WaveEnemyKind.Ranged)]
        public void SixKindsWalkAndKeepHitDeathFallback(WaveEnemyKind kind)
        {
            Assert.That(EnemyRigAtlas.SelectPose(kind, "approach", true, "walk"), Is.EqualTo(EnemyRigPose.Walk));
            Assert.That(EnemyRigAtlas.SelectPose(kind, "tell-charge", true, "hit"), Is.EqualTo(kind == WaveEnemyKind.Charger ? EnemyRigPose.Idle : EnemyRigPose.Legacy));
            Assert.That(EnemyRigAtlas.SelectPose(kind, "charge", true, "death"), Is.EqualTo(kind == WaveEnemyKind.Charger ? EnemyRigPose.Idle : EnemyRigPose.Legacy));
        }
        [Test] public void ShieldWalkAndChargerChargeDoNotDependOnLegacyStateNames()
        {
            Assert.That(EnemyRigAtlas.SelectPose(WaveEnemyKind.Shield, "approach", true, "guard"), Is.EqualTo(EnemyRigPose.Walk));
            Assert.That(EnemyRigAtlas.SelectPose(WaveEnemyKind.Charger, "charge", false, "idle"), Is.EqualTo(EnemyRigPose.Walk));
            Assert.That(EnemyRigAtlas.SelectPose(WaveEnemyKind.Charger, "tell-charge", true, "windup"), Is.EqualTo(EnemyRigPose.Tell));
            Assert.That(EnemyRigAtlas.SelectPose(WaveEnemyKind.FloodBoss, "charge", true, "charge"), Is.EqualTo(EnemyRigPose.Legacy));
        }
        [TestCase("approach", false)] [TestCase("recovery", false)] [TestCase("tell-charge", true)] [TestCase("charge", true)]
        public void OnlyActualChargeDangerColorsChargerRed(string phase, bool danger)
            => Assert.That(EnemyRigAtlas.IsChargeDanger(WaveEnemyKind.Charger, phase, "walk"), Is.EqualTo(danger));
        [TestCase("tell-charge", "hit")] [TestCase("charge", "hit")]
        [TestCase("tell-charge", "death")] [TestCase("charge", "death")]
        public void ChargerHitAndDeathRemainDarkEvenDuringDangerPhase(string phase, string state)
        {
            Assert.That(EnemyRigAtlas.SelectPose(WaveEnemyKind.Charger, phase, true, state), Is.EqualTo(EnemyRigPose.Idle));
            Assert.That(EnemyRigAtlas.IsChargeDanger(WaveEnemyKind.Charger, phase, state), Is.False);
        }
        [Test] public void AtlasFramesAreBoundedAndTellDoesNotLoop()
        {
            for (var frame = 0; frame < 10000; frame++)
                Assert.That(EnemyRigAtlas.FrameIndex(EnemyRigPose.Walk, frame / 60f, 80, 0), Is.InRange(0, 7));
            Assert.That(EnemyRigAtlas.FrameIndex(EnemyRigPose.Tell, 999, 80, .49f), Is.Zero);
            Assert.That(EnemyRigAtlas.FrameIndex(EnemyRigPose.Tell, 999, 80, .5f), Is.EqualTo(1));
            Assert.That(EnemyRigAtlas.FrameIndex(EnemyRigPose.Tell, 999, 80, 2), Is.EqualTo(1));
        }
    }
}
