using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        readonly EnemyDeathPresentationPool enemyDeaths = new EnemyDeathPresentationPool();
        // Last two rendered enemy poses, swapped per presented frame. Wave kills arrive after Core already removed the actor.
        readonly Dictionary<int, DyingEnemyPose>[] renderedEnemies =
            { new Dictionary<int, DyingEnemyPose>(1024), new Dictionary<int, DyingEnemyPose>(1024) };
        readonly List<int> vanishedEnemies = new List<int>(256);
        int renderedEnemyFrame;
        public int ActiveEnemyDeaths => enemyDeaths.Count;
        public int FullEnemyDeaths => enemyDeaths.FullCount;
        public int PeakFullEnemyDeaths => enemyDeaths.PeakFull;
        public int OverflowedEnemyDeaths => enemyDeaths.Overflowed;
        public int DroppedEnemyDeaths => enemyDeaths.Dropped;
        public bool EnemyDeathPresented(int id) => enemyDeaths.Contains(id);
        bool EnemyDeathsActive => wave != null && ImpactFeedbackEnabled;
        Dictionary<int, DyingEnemyPose> RenderedNow => renderedEnemies[renderedEnemyFrame & 1];
        Dictionary<int, DyingEnemyPose> RenderedBefore => renderedEnemies[(renderedEnemyFrame + 1) & 1];

        void ResetEnemyDeaths()
        {
            enemyDeaths.Clear(); renderedEnemies[0].Clear(); renderedEnemies[1].Clear(); vanishedEnemies.Clear();
        }

        void RecordRenderedEnemy(int id, in DyingEnemyPose pose)
        {
            if (!EnemyDeathsActive) return;
            RenderedNow[id] = pose;
            if (enemyDeaths.Contains(id)) enemyDeaths.Release(id);
        }

        // Kill reaches the screen: arm the held actor (or its last rendered pose) instead of letting it vanish.
        void AcceptEnemyDeath(WaveEvent value, Vector2 origin)
        {
            var hasPose = RenderedBefore.TryGetValue(value.SubjectId, out var pose);
            var at = hasPose ? pose.Actor.Position : origin;
            var delay = 0f;
            var direction = at - (PredictedLordPosition ?? RenderedLordPosition);
            if (M1BladeKill(value, origin, at, hasPose, out var bladeDelay, out var bladeDirection)) { delay = bladeDelay; direction = bladeDirection; }
            else if (LightningKillDelay(value, out var lightningDelay)) delay = lightningDelay;
            var tier = enemyDeaths.Kill(value.SubjectId, value.Id, pose, hasPose, direction, visualTime, delay, value.Amount > 0);
            if (tier == EnemyDeathTier.None) impactParticles.Kill(value.Id, origin, value.Amount > 0);
        }

        // Enemies rendered last frame, absent now and killed in the current snapshot are held until that kill reaches the screen.
        // Other absences (relocation, despawn, snapshot reset) vanish as before.
        void FinishRenderedEnemies()
        {
            if (!EnemyDeathsActive) { renderedEnemies[0].Clear(); renderedEnemies[1].Clear(); return; }
            vanishedEnemies.Clear();
            foreach (var id in RenderedBefore.Keys) if (!RenderedNow.ContainsKey(id) && waveKilledIds.Contains(id) && !enemyDeaths.Contains(id)) vanishedEnemies.Add(id);
            for (var i = 0; i < vanishedEnemies.Count; i++) enemyDeaths.Hold(vanishedEnemies[i], RenderedBefore[vanishedEnemies[i]], visualTime);
            renderedEnemyFrame++;
            RenderedNow.Clear();
        }

        void DrawEnemyDeaths()
        {
            if (!EnemyDeathsActive) return;
            for (var i = 0; i < enemyDeaths.Count; i++)
            {
                if (enemyDeaths.TakeBurst(i, visualTime, out var eventId, out var origin, out var experience)) impactParticles.Kill(eventId, origin, experience);
                var sample = enemyDeaths.Sample(i, visualTime, KnockWorldUnits);
                ref readonly var pose = ref enemyDeaths.Pose(i);
                var actor = pose.Actor;
                if (actor.Visual != null && sample.ActorOpacity > 0)
                {
                    var body = new ActorPose(actor.Body.Offset, actor.Body.Scale * (1 + sample.Pop), actor.Body.Degrees);
                    var tint = pose.Tint; tint.a *= sample.ActorOpacity;
                    DrawActor(actor.Visual, pose.Layer, sample.Position, actor.AnimationTime, actor.Dimensions, false, -1, -1, tint: tint, flash: sample.Flash,
                        upperOpacity: pose.UpperOpacity, facing: actor.Facing, poseOverride: body, uv: actor.Rigged ? actor.Uv : (Rect?)null);
                    if (!actor.Rigged)
                    {
                        var edge = GameVisualTokens.Ink; edge.a *= sample.ActorOpacity;
                        DrawActor(actor.Visual, pose.Layer, sample.Position, actor.AnimationTime, actor.Dimensions, false, -1, -1, tint: edge,
                            upperOpacity: pose.UpperOpacity, facing: actor.Facing, edgeTexels: GameVisualTokens.WaveEdgeTexels, poseOverride: body);
                    }
                }
                if (sample.FallOpacity > 0 && pose.DefinitionId != null)
                {
                    var fallen = EnemyDeathVisual(pose.DefinitionId);
                    Draw(fallen, pose.Layer, sample.Position, 0, WaveEnemySize(fallen.WorldSize, pose.Kind), opacity: sample.FallOpacity, tint: pose.Tint, applyTween: false);
                }
            }
        }
    }
}
