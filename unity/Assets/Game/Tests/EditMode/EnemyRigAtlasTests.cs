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


        // #399: the two approved replacements keep their 640ms cycle; untouched species retain their own timing.
        [Test]
        public void BakedRigsKeepTimeBasedWalkWithOnlyApprovedSixteenFrameReplacements()
        {
            var m = UnityEngine.JsonUtility.FromJson<ArtManifest>(System.IO.File.ReadAllText(System.IO.Path.Combine(UnityEngine.Application.dataPath, "Art", "wave1a-manifest.json")));
            var walks = System.Array.FindAll(m.roles, r => r.id.StartsWith("enemy.rig.", System.StringComparison.Ordinal) && r.id.EndsWith(".walk", System.StringComparison.Ordinal));
            Assert.That(walks.Length, Is.EqualTo(6), "six baked rigs");
            foreach (var walk in walks)
            {
                var approved = walk.id == "enemy.rig.shield_raider.walk" || walk.id == "enemy.rig.ram_runner.walk";
                var count = approved ? 16 : 8;
                Assert.That((walk.frames.Length, walk.cyclePixels), Is.EqualTo((count, 0f)), walk.id);
                var expectedMs = approved || walk.id == "enemy.rig.wine_wasp.walk" ? 40
                    : walk.id == "enemy.rig.seed_mite.walk" ? 55 : 80;
                Assert.That(walk.frameMs, Is.EqualTo(expectedMs), walk.id);
                Assert.That(walk.atlas, Is.EqualTo("fallow-enemy-rigs"), "retain one shared texture");
                Assert.That(walk.tween, Is.EqualTo("none"), walk.id);
                for (var frame = 0; frame < 2000; frame++)
                {
                    var time = frame / 60f;
                    Assert.That(EnemyRigAtlas.FrameIndex(EnemyRigPose.Walk, time, walk.frameMs, 0, walk.frames.Length),
                        Is.EqualTo(UnityEngine.Mathf.FloorToInt(time * 1000 / walk.frameMs) % count), walk.id);
                }
            }
        }

        // A sixteen-frame sheet with a cycle length plays by distance: one stride is one cycle across the sixteen frames, a standing
        // enemy holds its frame, and a fast move is capped at the walk rate.
        [Test]
        public void SixteenFrameWalkWithACycleLengthPlaysByDistance()
        {
            var walk = new PersonSheetWalk(); const float stride = 1.6f; var max = GameVisualTokens.EnemyWalkMaxCyclesPerSecond;
            var at = UnityEngine.Vector2.zero; var time = 0f;
            Assert.That(PersonSheetWalk.FrameAt(walk.Phase("enemy", 7, at, time, stride, max, 1), 16), Is.EqualTo(0));
            for (var step = 1; step <= 16; step++)
            {
                at += UnityEngine.Vector2.right * (stride / 16); time += 1f;
                Assert.That(PersonSheetWalk.FrameAt(walk.Phase("enemy", 7, at, time, stride, max, 1), 16), Is.EqualTo(step % 16));
            }
            time += 1f;
            Assert.That(PersonSheetWalk.FrameAt(walk.Phase("enemy", 7, at, time, stride, max, 1), 16), Is.EqualTo(0), "standing holds the frame");
            var before = walk.Phase("enemy", 8, at, time, stride, max, 1);
            var after = walk.Phase("enemy", 8, at + UnityEngine.Vector2.right * 100, time + .1f, stride, max, 1);
            Assert.That(UnityEngine.Mathf.Repeat(after - before, 1), Is.EqualTo(max * .1f).Within(1e-4f), "capped at the walk rate");
            Assert.That(EnemyRigAtlas.FrameIndex(EnemyRigPose.Walk, 1.6f, 100, 0, 16), Is.EqualTo(0));
            Assert.That(EnemyRigAtlas.FrameIndex(EnemyRigPose.Walk, 1.5f, 100, 0, 16), Is.EqualTo(15));
        }
    }
}
