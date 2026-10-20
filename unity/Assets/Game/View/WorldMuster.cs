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
        float heroDrawnHeight;   // the knight as drawn (sheet figure, or the rig figure): the damage-number keep-out uses it
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
            heroDrawnHeight = heroFigureHeight;
            var push = heroBodyWidth * HeroHitFeedback.PushBodyFraction;
            if (heroSheetOn)
            {
                // The knight sprite sheet (#268): its role's world size is the whole cell, not the drawn knight, so the box that
                // pillars and soldiers keep clear of is the figure footprint standing on the foot baseline (HeroSheetSpec.FigureSize,
                // the shadow and camera margin size). heroBodyWidth and heroFigureHeight stay as before: they size weapon effects.
                var figure = HeroSheetSpec.FigureSize(hero);
                heroDrawnHeight = figure.y;
                box = new Rect(anchor.x - figure.x * .5f, anchor.y, figure.x, figure.y);
            }
            box = Rect.MinMaxRect(box.xMin - push, box.yMin - push, box.xMax + push, box.yMax + push);
            RenderedHeroSilhouette = box;
            heroSilhouetteLocal = new Rect(box.position - anchor, box.size);
            heroSilhouetteKnown = true;
        }

        // Damage numbers keep off the hero (WorldDamageNumbers.HeroKeepOutOfFigureHeight): a circle round the drawn knight's centre.
        void UpdateNumberKeepOut()
        {
            var known = heroSilhouetteKnown && hasRenderedLord;
            var centre = (PredictedLordPosition ?? RenderedLordPosition) + Vector2.up * heroDrawnHeight * .5f;
            var radius = known ? heroDrawnHeight * WorldDamageNumbers.HeroKeepOutOfFigureHeight : 0;
            numbers?.SetHero(centre, radius); lordDamageNumbers?.SetHero(centre, radius);
        }

        static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        // Muster horn sound wave (Astra, Release muster-horn-wave-20261020; user decisions 2026-10-20): the painted wave replaces the
        // two outline rings, drawn under the hero and the enemies as Astra specified (the user chose it over drawing it above the
        // enemies). It grows and fades as HornWaveSpec gives for its stage; its worldSize is in units of the painted outer edge, so
        // that edge lands on the Core radius. Display only: the Core hit tick is unchanged.
        ArtVisual hornWaveLevel1, hornWaveEvolved; bool hornWaveChecked;
        public int HornWavesDrawn { get; private set; }

        // Whether source's attack is the horn's call, and at which stage: null for any other attack. The review composition is the
        // "summon" shape; normal play is the horn's MusterWave gear or one of its evolutions.
        bool? HornWaveStage(string source)
        {
            if (source == null || waveDefinition == null) return null;
            if (m1CombatShapes.TryGetValue(source, out var shape)) return shape == "summon" ? WeaponEvolved(source) : (bool?)null;
            var evolution = waveDefinition.Evolutions.ContainsKey(source);
            var baseSource = attackSources.TryGetValue(source, out var mapped) ? mapped : source;
            if (baseSource != "core:muster_horn" || !waveDefinition.Gear.TryGetValue(baseSource, out var gear) || gear.Kind != WaveAttackKind.MusterWave) return null;
            return evolution;
        }

        bool DrawHornWave(Vector2 center, float radius, float age, bool evolved)
        {
            if (!hornWaveChecked)
            {
                hornWaveChecked = true;
                if (art != null) { art.TryResolveRole(HornWaveSpec.Level1Role, out hornWaveLevel1); art.TryResolveRole(HornWaveSpec.EvolvedRole, out hornWaveEvolved); }
            }
            var visual = evolved && hornWaveEvolved != null ? hornWaveEvolved : hornWaveLevel1;
            if (visual == null) return false;
            var alpha = HornWaveSpec.Opacity(age, evolved);
            if (radius <= 0 || alpha <= 0) return true;
            Draw(visual, GameVisualTokens.AttackLayer, center, 0, visual.WorldSize * radius * HornWaveSpec.Share(age, evolved), 0, alpha, applyTween: false);
            HornWavesDrawn++;
            return true;
        }

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

    // Muster horn sound wave values (WorldRenderer.DrawHornWave). Roles from tools/art-authoring/horn-wave/pack.mjs.
    // Level 1, as the inner outline ring it replaced: 0.3 -> 1.0 of the Core radius over 0.18 s while fading out. Evolved (user
    // decision 2026-10-20): its radius 6.0 (about 1,120 px on the review screen) starts past the portrait half-width even at 0.3,
    // so it grows from 0.05 to 1.0 over 0.30 s and fades out over the last 0.08 s, so it is seen leaving the hero.
    public static class HornWaveSpec
    {
        public const string Level1Role = "fx.muster_horn.wave.lv1", EvolvedRole = "fx.muster_horn.wave.evolved";
        public const float Level1StartShare = .3f, Level1Seconds = .18f;
        public const float EvolvedStartShare = .05f, EvolvedSeconds = .30f, EvolvedFadeSeconds = .08f;
        public static float Seconds(bool evolved) => evolved ? EvolvedSeconds : Level1Seconds;
        public static float Share(float age, bool evolved)
            => Mathf.Lerp(evolved ? EvolvedStartShare : Level1StartShare, 1, Mathf.Clamp01(age / Seconds(evolved)));
        public static float Opacity(float age, bool evolved)
            => evolved ? 1 - Mathf.Clamp01((age - (EvolvedSeconds - EvolvedFadeSeconds)) / EvolvedFadeSeconds) : 1 - Mathf.Clamp01(age / Level1Seconds);
    }
}
