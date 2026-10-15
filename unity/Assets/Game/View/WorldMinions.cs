using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // Wave muster groups drawn as moving soldiers (MinionSquad). Core group positions anchor them and Core group hits trigger
    // the thrust frame; everything here is presentation.
    public sealed partial class WorldRenderer
    {
        public static readonly Color MinionTint = new Color(.66f, .71f, .82f);   // with MinionWash: desaturated blue, allies below own attacks (P5)
        public const float MinionWash = .3f, BannerWash = .85f;
        readonly MinionSquad minions = new MinionSquad();
        readonly List<(int group, Vector2 at)> pendingGroupHits = new List<(int, Vector2)>(32);
        readonly Dictionary<int, float> groupLastHit = new Dictionary<int, float>(16);
        readonly Dictionary<int, float> chargeShoveUntil = new Dictionary<int, float>(256);
        float minionsSteppedAt = float.NegativeInfinity;
        public long MinionAgentFrames => minions.AgentFrames;
        public long MinionStationaryFrames => minions.StationaryFrames;
        public int MinionGroupHits => minions.GroupHits;
        public int MinionThrustsOnHitFrame => minions.ThrustsOnHitFrame;
        public int MinionCount => minions.Count;
        public int MinionBearerFrames { get; private set; }
        public int MinionChargeShoves { get; private set; }
        public System.Action<Rect, MinionState> RenderedMinionBounds;
        public System.Action<int, int> MinionThrustSample;   // group, soldiers that thrust on this frame
        public System.Action<int> MinionHitSample;           // group whose Core hit is presented on the next frame

        void ResetMinions()
        {
            minions.Clear(); pendingGroupHits.Clear(); groupLastHit.Clear(); chargeShoveUntil.Clear();
            minionsSteppedAt = float.NegativeInfinity; MinionBearerFrames = MinionChargeShoves = 0;
        }

        void QueueGroupHit(int group, Vector2 at)
        {
            MinionHitSample?.Invoke(group);
            if (pendingGroupHits.Count < pendingGroupHits.Capacity) pendingGroupHits.Add((group, at));
        }

        ArtVisual MinionArt(int index) => Resolve("person", (index & 1) == 0 ? "militia" : "guard", "muster");

        Vector2? NearestEnemy(Vector2 from, float range)
        {
            Vector2? best = null; var bestDistance = range * range;
            if (indexedWaveActors == null) return null;
            for (var i = 0; i < indexedWaveActors.Enemies.Count; i++)
            {
                var at = Point(indexedWaveActors.Enemies[i].Position); var distance = (at - from).sqrMagnitude;
                if (distance <= bestDistance) { bestDistance = distance; best = at; }
            }
            return best;
        }

        // Called from DrawWave for every Core group; draws nothing itself.
        void SyncMinionGroup(WaveGroupView group, Vector2 anchor, Vector2 lordPoint, int tickRate)
        {
            var formation = WaveMusterFormation(group, group.Training > 0 ? 2 : 1);
            var gear = waveDefinition.Gear[group.Source]; WaveGearDefinition reference = null;
            GearReference?.Gear.TryGetValue(group.Source, out reference);
            var level = EquipmentLevel(group.Source);
            var charge = WeaponEvolutionGrade.Active(WeaponEvolutionGrade.RangeRatio(gear, level, reference),
                MusterFormationRule.ActiveWaveEvolution(wave.Evolutions, waveDefinition.Evolutions, group.Source));
            var engaging = group.Phase != "returning" && group.Phase != "idle";
            var target = engaging ? NearestEnemy(anchor, gear.Range / (float)settings.WorldUnitsPerUnityUnit) : null;
            var nextHit = -1f;
            if (engaging && groupLastHit.TryGetValue(group.Id, out var last)) nextHit = last + gear.CooldownTicks / (float)Mathf.Max(1, tickRate) - visualTime;
            minions.SyncGroup(new MinionSquad.GroupInput { Id = group.Id, Count = formation.Soldiers, Mode = engaging ? MinionMode.Seek : MinionMode.Return,
                Anchor = anchor, Target = target ?? anchor, HasTarget = target.HasValue, Hero = lordPoint, Charge = charge, NextHitIn = nextHit, Scale = formation.Scale }, visualTime);
        }

        float MinionBodyWidth => MinionArt(0).WorldSize.x * .8f;

        void StepAndDrawMinions()
        {
            minions.EndSync(visualTime);
            for (var i = 0; i < pendingGroupHits.Count; i++)
            {
                var thrusts = minions.GroupHit(pendingGroupHits[i].group, pendingGroupHits[i].at, visualTime);
                MinionThrustSample?.Invoke(pendingGroupHits[i].group, thrusts);
                groupLastHit[pendingGroupHits[i].group] = visualTime;
            }
            pendingGroupHits.Clear();
            var dt = float.IsNegativeInfinity(minionsSteppedAt) ? 0 : visualTime - minionsSteppedAt;
            minionsSteppedAt = visualTime;
            var w = MinionBodyWidth;
            minions.Step(visualTime, dt, w);
            var hero = new Rect(heroSilhouetteLocal.position + RenderedLordPosition, heroSilhouetteLocal.size);
            var clearance = heroBodyWidth * MusterFormationRule.HeroClearanceBodyWidths;
            for (var i = 0; i < minions.Count; i++) DrawMinion(minions[i], w, hero, clearance);
        }

        void DrawMinion(in MinionAgent a, float w, Rect hero, float clearance)
        {
            var art = MinionArt(a.Index);
            var size = art.WorldSize * Mathf.Max(.01f, a.Scale);
            var direction = a.Facing.sqrMagnitude > .000001f ? a.Facing : Vector2.right;
            var offset = Vector2.zero; var degrees = 0f; var stretch = Vector2.one; var opacity = 1f;
            var speed = (a.Position - a.Previous).magnitude;
            if (speed > MinionSpec.StationaryStep * w)
            {
                // Procedural run: two bounces per stride, lean into the run.
                offset.y += Mathf.Abs(Mathf.Sin(a.Stride * Mathf.PI * 1.6f)) * .07f * size.y;
                degrees -= Mathf.Sign(direction.x) * 7;
                stretch.y *= 1 - .04f * Mathf.Abs(Mathf.Cos(a.Stride * Mathf.PI * 1.6f));
            }
            var phase = a.AttackPhase(visualTime);
            var age = a.AttackAge(visualTime);
            var windup = MinionSpec.Seconds(MinionSpec.WindupFrames); var thrust = MinionSpec.Seconds(MinionSpec.ThrustFrames);
            if (phase == 0) { offset -= direction * (.14f * w * Mathf.Clamp01(age / windup)); degrees += Mathf.Sign(direction.x) * 9; stretch.y *= .95f; }
            else if (phase == 1) { offset += direction * (.4f * w); stretch.x *= 1.07f; }
            else if (phase == 2) offset += direction * (.4f * w * (1 - Mathf.Clamp01((age - windup - thrust) / MinionSpec.Seconds(MinionSpec.RecoverFrames))));
            if (a.State == MinionState.Spawn)
            {
                var t = Mathf.Clamp01((visualTime - a.StateStarted) / MinionSpec.Seconds(MinionSpec.SpawnFrames));
                stretch *= t < .7f ? Mathf.Lerp(.4f, 1.08f, t / .7f) : Mathf.Lerp(1.08f, 1, (t - .7f) / .3f);
                DrawMinionDust(a.Position, size, 1 - t, .6f + t);
            }
            if (a.State == MinionState.Despawn)
            {
                var t = Mathf.Clamp01((visualTime - a.StateStarted) / MinionSpec.DespawnSeconds);
                opacity = 1 - t; offset.y += t * .15f * size.y;
                DrawMinionDust(a.Position, size, 1 - t, 1 + t * .8f);
            }
            var at = a.Position + offset;
            var drawn = Vector2.Scale(size, stretch);
            if (heroSilhouetteKnown) at = MusterFormationRule.ClearOfHero(at, drawn, art.Pivot, hero, clearance, direction);
            var tint = MinionTint; tint.a = opacity;
            var facing = new ActorFacingPose(direction.x < 0 ? 1 : 0, 0);
            DrawActor(art, GameVisualTokens.AllyLayer, at, visualTime + a.Index * .07f, drawn, false, -1, -1, tint: tint, flash: MinionWash, facing: facing,
                poseOverride: new ActorPose(Vector2.zero, Vector2.one, degrees));
            RenderedMinionBounds?.Invoke(MusterFormationRule.SpriteRect(at, drawn, art.Pivot), a.State);
            if (phase == 1 || phase == 2 && age < windup + thrust + MinionSpec.Seconds(2))
            {
                // The spear itself drives out along the thrust so the attack reads at a glance.
                var spear = Resolve("icon", "core:ditch_pike", "default");
                var reach = size.y * (phase == 1 ? 1 : .8f);
                var hand = at + direction * (.1f * w) + Vector2.up * size.y * .35f;
                var tip = hand + direction * reach;
                var shaft = Rect.MinMaxRect(Mathf.Min(hand.x, tip.x), Mathf.Min(hand.y, tip.y), Mathf.Max(hand.x, tip.x), Mathf.Max(hand.y, tip.y));
                if (!heroSilhouetteKnown || !shaft.Overlaps(hero))
                {
                    M1CombatSpearGeometry.PoseAlong(hand, direction, reach, spear.Pivot, out var spearAt, out var spearSize, out var spearDegrees);
                    Draw(spear, GameVisualTokens.AllyLayer + 1, spearAt, 0, spearSize, spearDegrees, opacity, Color.white, applyTween: false);
                    RenderedMinionBounds?.Invoke(shaft, a.State);
                }
            }
            if (a.Charge && a.Index == 0 && a.State != MinionState.Despawn)
            {
                // Evolution element: the flag bearer leads the charge under a gold banner.
                var banner = Resolve("icon", "core:levy_banner", "default");
                var bannerSize = banner.WorldSize * (size.y * .62f / Mathf.Max(banner.WorldSize.x, banner.WorldSize.y));
                // Washed toward white before tinting so the blue banner art reads as a gold flag.
                Draw(banner, GameVisualTokens.AllyLayer + 1, at + Vector2.up * size.y * .95f, 0, bannerSize, Mathf.Sin(visualTime * 7) * 6, opacity, GameVisualTokens.Ready, BannerWash, applyTween: false);
                MinionBearerFrames++;
            }
            if (a.Charge && a.Index <= 2 && (a.State == MinionState.Seek || a.State == MinionState.Attack)) ShoveEnemiesNear(at, direction, w);
        }

        void DrawMinionDust(Vector2 at, Vector2 size, float opacity, float scale)
        {
            if (opacity <= 0) return;
            var dust = Resolve("feedback", "death", "default");
            var dustSize = dust.WorldSize * (size.x * .9f * scale / Mathf.Max(dust.WorldSize.x, dust.WorldSize.y));
            var color = new Color(.78f, .76f, .7f, opacity * .7f);
            Draw(dust, GameVisualTokens.AllyLayer - 1, at, 0, dustSize, 0, 1, color, applyTween: false);
        }

        // The charge pushes enemies in its path aside: a shove without the white "damaged" flash, Core positions unchanged.
        void ShoveEnemiesNear(Vector2 at, Vector2 forward, float w)
        {
            var radius = .8f * w;
            if (indexedWaveActors == null) return;
            for (var i = 0; i < indexedWaveActors.Enemies.Count; i++)
            {
                var enemy = indexedWaveActors.Enemies[i];
                var position = Point(enemy.Position);
                if ((position - at).sqrMagnitude > radius * radius) continue;
                if (chargeShoveUntil.TryGetValue(enemy.Id, out var until) && visualTime < until) continue;
                if (chargeShoveUntil.Count > 512) chargeShoveUntil.Clear();
                chargeShoveUntil[enemy.Id] = visualTime + .35f;
                var side = new Vector2(-forward.y, forward.x);
                var away = Vector2.Dot(position - at, side) >= 0 ? side : -side;
                enemyImpactPoses.Hit(enemy.Id, visualTime, (forward + away).normalized, MinionSpec.Seconds(2), 0, false);
                MinionChargeShoves++;
            }
        }
    }
}
