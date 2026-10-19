using System;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        readonly ImpactFeedbackPool impactParticles = new ImpactFeedbackPool();
        readonly EnemyImpactPosePool enemyImpactPoses = new EnemyImpactPosePool();
        bool impactFeedbackEnabled = true;
        float impactShakeUntil;
        ArtVisual impactShard, impactDust, impactExperience;
        public bool ImpactFeedbackEnabled
        {
            get => impactFeedbackEnabled;
            set { impactFeedbackEnabled = value; if (!value) ResetImpactFeedback(); }
        }
        public int ActiveImpactParticles => impactParticles.Count;
        public int DroppedImpactParticles => impactParticles.Dropped;
        public int ActiveEnemyImpactPoses => enemyImpactPoses.Count;
        public int DroppedEnemyImpactPoses => enemyImpactPoses.Dropped;
        // Integration owns display-only frame holding. These signals must never pause Core or change its clock.
        public Action<float> BossHitStopRequested;
        // Display-only hitstop for big hits (evolution attacks), HitReaction.BigHitStopSeconds; the run's presentation timeline holds it.
        public Action<float> HitStopRequested;
        public int BigHitStops { get; private set; }
        float lastBigHitStop = float.NegativeInfinity;
        public Action<float> BossDeathSlowRequested;

        void InitializeImpactFeedback()
        {
            impactShard = Resolve("feedback", "hit", "default");
            impactDust = Resolve("feedback", "death", "default");
            impactExperience = Resolve("feedback", "kill-experience", "default");
            PrepareImpactBatch(impactShard); PrepareImpactBatch(impactDust); PrepareImpactBatch(impactExperience);
        }

        void PrepareImpactBatch(ArtVisual visual)
        {
            var key = (GameVisualTokens.WaveTransientCueLayer, visual.Texture);
            if (!batches.ContainsKey(key)) batches.Add(key, new SpriteBatch(visual.Texture, shader, renderCamera, key.Item1));
        }

        void ResetImpactFeedback()
        {
            impactParticles.Clear(); enemyImpactPoses.Clear(); impactShakeUntil = 0; lastBigHitStop = float.NegativeInfinity;
            ResetEnemyDeaths(); ResetM1Blades(); ResetLightning(); ResetArrows(); ResetPikes(); ResetSporePatches(); ResetSling(); ResetOrbits(); heroHitFeedback.Reset();
        }

        void AcceptImpactFeedback(WaveEvent value)
        {
            if (!ImpactFeedbackEnabled) return;
            var origin = Point(value.Position);
            if (value.Amount > 0 && (value.Kind == "hit" ||
                ((value.Kind == "group-hit" || value.Kind == "trained-group-hit") && waveDefinition.Gear.ContainsKey(value.Source))))
            {
                if (value.Kind == "hit" && (DeferChainHit(value, origin) || DeferBladeHit(value, origin))) return;
                var direction = origin - (PredictedLordPosition ?? RenderedLordPosition);
                if (direction.sqrMagnitude < .000001f) direction = Vector2.up;
                if (SporePatchHitDirection(value, origin, out var patchDirection))
                    enemyImpactPoses.Hit(value.SubjectId, visualTime, patchDirection, 0, 0, true);
                else if (OrbitHitReaction(value, origin)) { }
                else if (ArrowHitDirection(value, origin, out var arrowDirection))
                    enemyImpactPoses.Hit(value.SubjectId, visualTime, arrowDirection, ArrowSpec.Seconds(ArrowSpec.HitHoldFrames), 0, true);
                else if (PikeHitDirection(value, origin, out var pikeDirection))
                    enemyImpactPoses.Hit(value.SubjectId, visualTime, pikeDirection, PikeSpec.Seconds(PikeSpec.HitHoldFrames), 0, true);
                else if (SlingHitDirection(value, origin, out var slingDirection))
                    enemyImpactPoses.Hit(value.SubjectId, visualTime, slingDirection, 0, 0, true);
                else enemyImpactPoses.Hit(value.SubjectId, visualTime, direction);
                impactParticles.Hit(value.Id, origin);
                if (value.Kind == "hit") RequestBigHitStop(value.Source);
                if (ShowDamageNumbers && showTransientText) numbers?.AddAggregated(value.SubjectId, origin, value.Amount);
            }
            else if (value.Kind == "enemy-killed")
            {
                AcceptEnemyDeath(value, origin);
                if (value.Source == waveDefinition.BossId) BossDeathSlowRequested?.Invoke(GameVisualTokens.BossDeathSlowSeconds);
            }
            else if (value.Kind == "evolution-activated" ||
                (value.Kind == "attack" && waveDefinition.Evolutions.ContainsKey(value.Source)))
                impactShakeUntil = visualTime + GameVisualTokens.ImpactShakeSeconds;
        }

        void AdvanceImpactFeedback(float seconds)
        {
            impactParticles.Advance(seconds);
            enemyImpactPoses.Advance(visualTime);
            enemyDeaths.Advance(visualTime);
            AdvanceLightning();
        }

        EnemyImpactPose ResolveEnemyImpactPose(int id, float bodyWidth, EnemyImpactPose pose)
        {
            return ImpactFeedbackEnabled ? enemyImpactPoses.Sample(id, visualTime, bodyWidth, HitReaction.KnockbackPixels * PixelsToWorld, pose) : pose;
        }

        // World units per screen pixel of the render camera (1 when there is no camera, as in some editor tests).
        float PixelsToWorld => renderCamera == null ? 1 : renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);

        // A big hit: an evolution's attack (runtime evolution, or a review case shown at its evolved display stage).
        bool BigHit(string source) => source != null && (waveDefinition.Evolutions.ContainsKey(source) || WeaponEvolved(source));
        // Called when a hit is presented (at once, or when a deferred blade or chain hit reaches the screen); the timeline's cooldown
        // keeps a stream of evolved hits from stopping the display continuously.
        void RequestBigHitStop(string source)
        {
            if (!BigHit(source) || visualTime - lastBigHitStop < HitReaction.BigHitStopMinIntervalSeconds) return;
            lastBigHitStop = visualTime; BigHitStops++; HitStopRequested?.Invoke(HitReaction.BigHitStopSeconds);
        }

        public static Color EnemyBodyTint(WaveEnemyKind kind)
        {
            switch (kind)
            {
                case WaveEnemyKind.Ranged: return new Color32(205, 191, 121, 255);
                case WaveEnemyKind.Charger: return new Color32(185, 165, 137, 255);
                case WaveEnemyKind.FloodBoss: return new Color32(175, 185, 195, 255);
                default: return new Color32(199, 206, 179, 255);
            }
        }

        Vector2 ImpactCameraOffset
        {
            get
            {
                if (!ShakeEnabled || !ImpactFeedbackEnabled || visualTime >= impactShakeUntil || renderCamera == null) return Vector2.zero;
                var remaining = (impactShakeUntil - visualTime) / GameVisualTokens.ImpactShakeSeconds;
                var pixelsToWorld = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
                return new Vector2(Mathf.Sin(remaining * Mathf.PI * 4), Mathf.Sin(remaining * Mathf.PI * 6)) * (2 * pixelsToWorld * remaining);
            }
        }

        void DrawImpactFeedback()
        {
            if (!ImpactFeedbackEnabled) return;
            for (var i = 0; i < impactParticles.Count; i++)
            {
                var particle = impactParticles[i];
                var visual = particle.Kind == ImpactParticleKind.Shard ? impactShard : particle.Kind == ImpactParticleKind.Dust ? impactDust : impactExperience;
                var size = particle.Size * (particle.Kind == ImpactParticleKind.Dust ? 1 + particle.Progress : 1);
                var opacity = 1 - particle.Progress;
                Draw(visual, GameVisualTokens.WaveTransientCueLayer, particle.Position, particle.Age,
                    visual.WorldSize * (size / Mathf.Max(visual.WorldSize.x, visual.WorldSize.y)), particle.Rotation,
                    opacity, tint: particle.Kind == ImpactParticleKind.Dust ? GameVisualTokens.Ink : GameVisualTokens.Attack,
                    applyTween: false);
            }
        }
    }
}
