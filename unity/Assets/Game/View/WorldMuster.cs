using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        public const float MusterLungeSeconds = .22f;
        // Canonical gear table used as the size reference; null means the run's own (canonical) wave definition.
        public WaveRuntimeDefinition GearReference { get; set; }
        public MusterPresentationPath MusterPath { get; private set; }
        public string MusterEvolutionId { get; private set; }
        public float MusterRatio { get; private set; } = 1;
        public int MusterSoldiers { get; private set; }
        public float MusterScale { get; private set; } = 1;
        public int MusterLunges { get; private set; }
        // Raised whenever the resolved path, evolution or ratio of a muster source changes; review logs record it.
        public Action<string, MusterFormation> MusterPresentationChanged;
        // Last drawn hero box relative to the hero anchor, and its body width; groups draw before the hero each frame.
        Rect heroSilhouetteLocal;
        const float HeroFigureOfRigCanvas = .62f;
        float heroFigureHeight;
        bool heroSilhouetteKnown;
        float heroBodyWidth;
        public Action<Rect> RenderedMusterSoldierBounds;
        public Rect RenderedHeroSilhouette { get; private set; }
        ShapeMeshes musterRingMeshes;
        ShapeBatch musterRings;
        readonly Dictionary<int, (float started, Vector2 target)> musterStrikes = new Dictionary<int, (float, Vector2)>(64);
        readonly Dictionary<string, (MusterPresentationPath path, string evolution, float ratio)> observedMuster =
            new Dictionary<string, (MusterPresentationPath, string, float)>(StringComparer.Ordinal);

        void ResetMuster()
        {
            musterStrikes.Clear(); observedMuster.Clear(); heroSilhouetteKnown = false; ResetMinions();
            MusterPath = MusterPresentationPath.None; MusterEvolutionId = null; MusterRatio = 1; MusterSoldiers = 0; MusterScale = 1; MusterLunges = 0;
        }

        // 0..1 growth of a gear over its canonical Lv1 (MusterFormationRule.Tier).
        float GearTier(string source)
        {
            var gear = waveDefinition.Gear[source];
            WaveGearDefinition reference = null; GearReference?.Gear.TryGetValue(gear.Id, out reference);
            return MusterFormationRule.Tier(gear, EquipmentLevel(gear.Id), reference);
        }

        int EquipmentLevel(string id)
        {
            if (acceptedFrame == null) return 1;
            for (var i = 0; i < acceptedFrame.Equipment.Count; i++) if (acceptedFrame.Equipment[i].Id == id) return acceptedFrame.Equipment[i].Level;
            return 1;
        }

        MusterFormation WaveMusterFormation(WaveGroupView group, int baseSoldiers)
        {
            var evolution = MusterFormationRule.ActiveWaveEvolution(wave.Evolutions, waveDefinition.Evolutions, group.Source);
            var ratio = 1f;
            if (waveDefinition.Gear.TryGetValue(group.Source, out var runtime))
            {
                WaveGearDefinition reference = null;
                GearReference?.Gear.TryGetValue(group.Source, out reference);
                ratio = MusterFormationRule.Ratio(runtime, EquipmentLevel(group.Source), reference);
            }
            var formation = MusterFormationRule.Resolve(baseSoldiers, ratio, evolution);
            ObserveMuster(group.Source, formation);
            return formation;
        }

        MusterFormation PeopleMusterFormation(PersonView person)
        {
            var formation = MusterFormationRule.Resolve(1, 1, MusterFormationRule.ActiveRuntimeEvolution(firstPlayable?.Evolutions, waveCatalog?.Runtime?.Evolutions, person.SourceId));
            if (formation.Path != MusterPresentationPath.None) ObserveMuster(person.SourceId, formation);
            return formation;
        }

        void ObserveMuster(string source, in MusterFormation formation)
        {
            if (observedMuster.TryGetValue(source, out var seen) && seen.path == formation.Path && seen.evolution == formation.EvolutionId &&
                Mathf.Approximately(seen.ratio, formation.Ratio)) return;
            observedMuster[source] = (formation.Path, formation.EvolutionId, formation.Ratio);
            MusterPath = formation.Path; MusterEvolutionId = formation.EvolutionId; MusterRatio = formation.Ratio;
            MusterSoldiers = formation.Soldiers; MusterScale = formation.Scale;
            MusterPresentationChanged?.Invoke(source, formation);
        }

        // A group strike carries the enemy position only; the attacking group is the nearest live group of that source.
        void AcceptWaveMusterStrike(WaveRuntimeFrame frame, WaveEvent value)
        {
            if (value.Amount <= 0 || (value.Kind != "group-hit" && value.Kind != "trained-group-hit")) return;
            var target = Point(value.Position); var best = float.PositiveInfinity; var id = -1;
            for (var i = 0; i < frame.Groups.Count; i++)
            {
                var group = frame.Groups[i];
                if (group.Source != value.Source) continue;
                var distance = (Point(group.Position) - target).sqrMagnitude;
                if (distance < best) { best = distance; id = group.Id; }
            }
            if (id >= 0) { QueueGroupHit(id, target); MusterLunges++; }
        }

        // First-playable soldiers attack through Core attack presentation events originating at the person.
        void AcceptPeopleMusterStrike(PresentationEvent effect)
        {
            if (acceptedFrame == null || effect.HitEntityIds == null || effect.HitEntityIds.Count == 0) return;
            if (MusterFormationRule.ActiveRuntimeEvolution(firstPlayable?.Evolutions, waveCatalog?.Runtime?.Evolutions, effect.SourceId) == null) return;
            Vector2? target = null;
            for (var i = 0; i < acceptedFrame.Enemies.Count && target == null; i++)
                if (acceptedFrame.Enemies[i].Id == effect.HitEntityIds[0]) target = Point(acceptedFrame.Enemies[i].Position);
            if (target == null) return;
            var origin = Point(effect.Origin); var best = float.PositiveInfinity; var id = -1;
            for (var i = 0; i < acceptedFrame.People.Count; i++)
            {
                var person = acceptedFrame.People[i];
                if (person.SourceId != effect.SourceId) continue;
                var distance = (Point(person.Position) - origin).sqrMagnitude;
                if (distance < best) { best = distance; id = person.Id; }
            }
            if (id >= 0) RecordMusterStrike(id, target.Value);
        }

        void RecordMusterStrike(int id, Vector2 target)
        {
            if (musterStrikes.Count >= 64 && !musterStrikes.ContainsKey(id)) musterStrikes.Clear();
            musterStrikes[id] = (visualTime, target);
            MusterLunges++;
        }

        void RecordHeroSilhouette(ArtVisual hero, Vector2 anchor)
        {
            // Union of the plain sprite and the taller live rig in both facings, widened by the hit push: never smaller than the figure.
            var rig = hero.WorldSize * (256f / 252) * GameVisualTokens.HeroRigHeightScale;
            var mirrored = new Vector2(1 - hero.Pivot.x, hero.Pivot.y); var rigMirrored = new Vector2(1 - HeroGaitState.Pivot.x, HeroGaitState.Pivot.y);
            var box = Union(Union(MusterFormationRule.SpriteRect(anchor, hero.WorldSize, hero.Pivot), MusterFormationRule.SpriteRect(anchor, hero.WorldSize, mirrored)),
                Union(MusterFormationRule.SpriteRect(anchor, rig, HeroGaitState.Pivot), MusterFormationRule.SpriteRect(anchor, rig, rigMirrored)));
            heroBodyWidth = hero.WorldSize.x * GameVisualTokens.HeroBodyWidthFraction;
            // The knight figure fills about 62% of its 512px rig canvas height (feet at 420, head near 100): the visible hero.
            heroFigureHeight = rig.y * HeroFigureOfRigCanvas;
            var push = heroBodyWidth * HeroHitFeedback.PushBodyFraction;
            if (heroSheetOn)
            {
                // The knight sprite sheet (#268): its role's world size is the whole cell, not the drawn knight, so the box that
                // pillars and soldiers keep clear of is the figure footprint standing on the foot baseline (HeroSheetSpec.FigureSize,
                // the shadow and camera margin size). heroBodyWidth and heroFigureHeight stay as before: they size weapon effects.
                var figure = HeroSheetSpec.FigureSize(hero);
                box = new Rect(anchor.x - figure.x * .5f, anchor.y, figure.x, figure.y);
            }
            box = Rect.MinMaxRect(box.xMin - push, box.yMin - push, box.xMax + push, box.yMax + push);
            RenderedHeroSilhouette = box;
            heroSilhouetteLocal = new Rect(box.position - anchor, box.size);
            heroSilhouetteKnown = true;
        }

        static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        void DrawMusterRing(Vector2 center, float radius, Color color, float opacity)
        {
            if (radius <= 0 || opacity <= 0) return;
            if (musterRings == null)
            {
                musterRingMeshes = new ShapeMeshes();
                musterRings = new ShapeBatch(musterRingMeshes[WorldShape.Circle], Resources.Load<Shader>("WorldShape"), renderCamera, GameVisualTokens.AttackLayer);
                musterRings.BeginFrame();
            }
            var pixels = renderCamera.orthographicSize * 2 / Mathf.Max(1, renderCamera.pixelHeight);
            color.a *= opacity;
            musterRings.Add(center, Vector2.one * radius, 0, color, Mathf.Clamp01(1 - pixels * 2.5f / radius));
        }

        // Draws a formation of soldiers; the representative keeps its authored size and slot when the formation is neutral.
        void DrawMusterFormation(int id, in MusterFormation formation, ArtVisual front, ArtVisual rear, int layer, Vector2 position, float time,
            bool walking, ActorFacingPose facing)
        {
            var size = front.WorldSize * formation.Scale;
            var lunge = Vector2.zero; var attack = -1f; var strikeTarget = Vector2.zero; var strikeAge = -1f;
            if (musterStrikes.TryGetValue(id, out var strike) && visualTime - strike.started < MusterLungeSeconds)
            {
                strikeAge = visualTime - strike.started; strikeTarget = strike.target; attack = strikeAge / MusterLungeSeconds;
                var toward = strike.target - position;
                if (toward.sqrMagnitude > .000001f) lunge = toward.normalized * (size.x * .35f * Mathf.Sin(Mathf.PI * attack));
            }
            var hero = new Rect(heroSilhouetteLocal.position + RenderedLordPosition, heroSilhouetteLocal.size);
            var clearance = heroBodyWidth * MusterFormationRule.HeroClearanceBodyWidths;
            var heading = attack >= 0 ? strikeTarget - hero.center : position - hero.center;
            var onHero = heroSilhouetteKnown && ((position - hero.center).magnitude < clearance + size.x || hero.Contains(position));
            for (var i = formation.Soldiers - 1; i >= 0; i--)
            {
                var art = (i & 1) == 0 ? front : rear;
                var soldierSize = art.WorldSize * formation.Scale;
                var slot = MusterFormationRule.Slot(i, size);
                var stagger = i * .025f;
                var reach = attack >= 0 ? lunge * Mathf.Clamp01(1 - stagger / MusterLungeSeconds) : Vector2.zero;
                var at = (onHero ? MusterFormationRule.FrontArc(i, formation.Soldiers, hero, heading, clearance + soldierSize.x * .5f) : position + slot) + reach;
                if (heroSilhouetteKnown) at = MusterFormationRule.ClearOfHero(at, soldierSize, art.Pivot, hero, clearance, heading);
                RenderedMusterSoldierBounds?.Invoke(MusterFormationRule.SpriteRect(at, soldierSize, art.Pivot));
                DrawActor(art, layer + (slot.y < 0 ? 1 : 0), at, time + i * .07f, soldierSize, walking,
                    attack >= 0 ? Mathf.Clamp01(attack - stagger) : -1, -1, facing: facing);
            }
            if (strikeAge < 0) return;
            var slash = Resolve("attack", "core:iron_blade", "sector90");
            var fade = 1 - Mathf.Clamp01(strikeAge / MusterLungeSeconds);
            var angle = Angle(strikeTarget - position);
            var reachSize = slash.WorldSize * (size.x * (formation.Evolved ? 1.6f : 1.1f));
            Draw(slash, GameVisualTokens.AttackLayer, position, strikeAge, reachSize, angle, fade * (formation.Evolved ? .9f : .6f),
                formation.Evolved ? GameVisualTokens.Ready : Color.white, applyTween: false);
            DrawWaveAttackEdge(slash, position, strikeAge, reachSize, angle, formation.Evolved ? GameVisualTokens.Ready : GameVisualTokens.Ally, fade);
        }
    }
}
