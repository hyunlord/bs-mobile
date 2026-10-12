using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        readonly ImpactFeedbackPool impactParticles = new ImpactFeedbackPool();
        readonly Dictionary<int, (float started, Vector2 direction)> impactRecoil = new Dictionary<int, (float, Vector2)>(1024);
        readonly List<int> expiredRecoil = new List<int>(1024);
        bool impactFeedbackEnabled = true;
        float impactShakeUntil;
        int impactBossId = -1;
        ArtVisual impactShard, impactDust, impactExperience;
        public bool ImpactFeedbackEnabled
        {
            get => impactFeedbackEnabled;
            set { impactFeedbackEnabled = value; if (!value) ResetImpactFeedback(); }
        }
        public int ActiveImpactParticles => impactParticles.Count;
        public int DroppedImpactParticles => impactParticles.Dropped;
        // Integration owns display-only frame holding. These signals must never pause Core or change its clock.
        public Action<float> BossHitStopRequested;
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
            impactParticles.Clear(); impactRecoil.Clear(); expiredRecoil.Clear(); impactShakeUntil = 0; impactBossId = -1;
        }

        void AcceptImpactFeedback(WaveEvent value)
        {
            if (!ImpactFeedbackEnabled) return;
            var origin = Point(value.Position);
            if (value.Kind == "enemy-spawn" && value.Source == waveDefinition.BossId) impactBossId = value.SubjectId;
            if (value.Kind == "hit")
            {
                var direction = origin - (PredictedLordPosition ?? RenderedLordPosition);
                if (direction.sqrMagnitude < .000001f) direction = Vector2.up;
                if (impactRecoil.Count < 1024 || impactRecoil.ContainsKey(value.SubjectId))
                    impactRecoil[value.SubjectId] = (visualTime, direction.normalized);
                impactParticles.Hit(value.Id, origin);
                if (ShowDamageNumbers && showTransientText) numbers?.AddAggregated(value.SubjectId, origin, value.Amount);
                if (value.SubjectId == impactBossId ||
                    (waveActorById.TryGetValue(value.SubjectId, out var actor) && actor.DefinitionId == waveDefinition.BossId))
                    BossHitStopRequested?.Invoke(GameVisualTokens.BossHitStopSeconds);
            }
            else if (value.Kind == "enemy-killed")
            {
                impactParticles.Kill(value.Id, origin, value.Amount > 0);
                if (value.Source == waveDefinition.BossId) BossDeathSlowRequested?.Invoke(GameVisualTokens.BossDeathSlowSeconds);
            }
            else if (value.Kind == "evolution-activated" ||
                (value.Kind == "attack" && waveDefinition.Evolutions.ContainsKey(value.Source)))
                impactShakeUntil = visualTime + GameVisualTokens.ImpactShakeSeconds;
        }

        void AdvanceImpactFeedback(float seconds)
        {
            impactParticles.Advance(seconds);
            expiredRecoil.Clear();
            foreach (var pair in impactRecoil)
                if (visualTime - pair.Value.started >= GameVisualTokens.EnemyRecoilSeconds) expiredRecoil.Add(pair.Key);
            for (var i = 0; i < expiredRecoil.Count; i++) impactRecoil.Remove(expiredRecoil[i]);
        }

        Vector2 EnemyImpactOffset(int id)
        {
            if (!ImpactFeedbackEnabled || !impactRecoil.TryGetValue(id, out var recoil)) return Vector2.zero;
            var progress = Mathf.Clamp01((visualTime - recoil.started) / GameVisualTokens.EnemyRecoilSeconds);
            var pixelsToWorld = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            return recoil.direction * (GameVisualTokens.EnemyRecoilPixels * pixelsToWorld * (1 - progress));
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
