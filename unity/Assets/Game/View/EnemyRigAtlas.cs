using System;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public enum EnemyRigPose { Legacy, Idle, Walk, Tell }

    // Authored parts are baked offline; runtime submits one quad for the entire actor.
    public sealed class EnemyRigAtlas
    {
        static readonly string[] Names = { "seed_mite", "crop_grazer", "raider", "shield_raider", "ram_runner", "wine_wasp" };
        readonly ArtVisual[,] visuals = new ArtVisual[6, 3];
        public EnemyRigAtlas(ArtCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            for (var i = 0; i < Names.Length; i++)
            {
                var prefix = "enemy.rig." + Names[i] + ".";
                visuals[i, 0] = catalog.ResolveRole(prefix + "idle");
                visuals[i, 1] = catalog.ResolveRole(prefix + "walk");
                visuals[i, 2] = catalog.ResolveRole(prefix + "tell");
                if (visuals[i, 0].UvRects.Count != 1 || visuals[i, 1].UvRects.Count != 8 || visuals[i, 2].UvRects.Count != 2)
                    throw new InvalidOperationException("Enemy rig must contain one idle, eight walk and two tell frames: " + Names[i]);
            }
        }
        static int Index(WaveEnemyKind kind) => kind switch
        {
            WaveEnemyKind.SeedThief => 0, WaveEnemyKind.RipeGrazer => 1, WaveEnemyKind.Pursuer => 2,
            WaveEnemyKind.Shield => 3, WaveEnemyKind.Charger => 4, WaveEnemyKind.Ranged => 5, _ => -1
        };
        public static EnemyRigPose SelectPose(WaveEnemyKind kind, string phase, bool walking, string fallback)
        {
            if (Index(kind) < 0) return EnemyRigPose.Legacy;
            if (fallback == "hit" || fallback == "death") return kind == WaveEnemyKind.Charger ? EnemyRigPose.Idle : EnemyRigPose.Legacy;
            if (phase != null && phase.StartsWith("tell-", StringComparison.Ordinal)) return EnemyRigPose.Tell;
            return walking || kind == WaveEnemyKind.Ranged || kind == WaveEnemyKind.Charger && phase == "charge"
                ? EnemyRigPose.Walk : EnemyRigPose.Idle;
        }
        public static bool IsChargeDanger(WaveEnemyKind kind, string phase, string state)
            => state != "hit" && state != "death" && kind == WaveEnemyKind.Charger && (phase == "tell-charge" || phase == "charge");
        public static int FrameIndex(EnemyRigPose pose, float time, int frameMilliseconds, float tellProgress)
        {
            if (frameMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(frameMilliseconds));
            if (pose == EnemyRigPose.Tell) return tellProgress < .5f ? 0 : 1;
            return pose == EnemyRigPose.Walk ? Mathf.FloorToInt(Mathf.Max(0, time) * 1000 / frameMilliseconds) % 8 : 0;
        }
        public bool TrySample(WaveEnemyKind kind, string phase, bool walking, string fallback, float time, float tellProgress, out ArtVisual visual, out Rect uv)
        {
            var pose = SelectPose(kind, phase, walking, fallback);
            if (pose == EnemyRigPose.Legacy) { visual = null; uv = default; return false; }
            visual = visuals[Index(kind), (int)pose - 1];
            uv = visual.UvRects[FrameIndex(pose, time, visual.FrameMs, tellProgress)];
            return true;
        }
    }
}
