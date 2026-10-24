using System;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public enum EnemyRigPose { Legacy, Idle, Walk, Tell }

    // Authored parts are baked offline; runtime submits one quad for the entire actor.
    // Walk frame counts come from the manifest per kind (#363). A walk role that states its cycle length (cyclePixels) plays by
    // distance moved like the hero and soldiers; the baked 8-frame rigs state none and keep their time-based walk unchanged.
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
                if (visuals[i, 0].UvRects.Count != 1 || visuals[i, 1].UvRects.Count < 2 || visuals[i, 2].UvRects.Count != 2)
                    throw new InvalidOperationException("Enemy rig must contain one idle, at least two walk and two tell frames: " + Names[i]);
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
        public static int FrameIndex(EnemyRigPose pose, float time, int frameMilliseconds, float tellProgress, int walkFrames = 8)
        {
            if (frameMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(frameMilliseconds));
            if (walkFrames < 1) throw new ArgumentOutOfRangeException(nameof(walkFrames));
            if (pose == EnemyRigPose.Tell) return tellProgress < .5f ? 0 : 1;
            return pose == EnemyRigPose.Walk ? Mathf.FloorToInt(Mathf.Max(0, time) * 1000 / frameMilliseconds) % walkFrames : 0;
        }
        // The kind's walk role, or null for kinds drawn without a rig.
        public ArtVisual WalkVisual(WaveEnemyKind kind) => Index(kind) < 0 ? null : visuals[Index(kind), 1];
        // walkFrame >= 0 is the distance-based walk frame for a walk role with cyclePixels; -1 keeps the time-based frame.
        public bool TrySample(WaveEnemyKind kind, string phase, bool walking, string fallback, float time, float tellProgress, out ArtVisual visual, out Rect uv, int walkFrame = -1)
        {
            var pose = SelectPose(kind, phase, walking, fallback);
            if (pose == EnemyRigPose.Legacy) { visual = null; uv = default; return false; }
            visual = visuals[Index(kind), (int)pose - 1];
            var frame = pose == EnemyRigPose.Walk && walkFrame >= 0 ? walkFrame % visual.UvRects.Count : FrameIndex(pose, time, visual.FrameMs, tellProgress, visual.UvRects.Count);
            uv = visual.UvRects[frame];
            return true;
        }
    }
}
