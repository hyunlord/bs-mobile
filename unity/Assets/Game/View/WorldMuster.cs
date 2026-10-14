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
        readonly Dictionary<int, (float started, Vector2 target)> musterStrikes = new Dictionary<int, (float, Vector2)>(64);
        readonly Dictionary<string, (MusterPresentationPath path, string evolution, float ratio)> observedMuster =
            new Dictionary<string, (MusterPresentationPath, string, float)>(StringComparer.Ordinal);

        void ResetMuster()
        {
            musterStrikes.Clear(); observedMuster.Clear();
            MusterPath = MusterPresentationPath.None; MusterEvolutionId = null; MusterRatio = 1; MusterSoldiers = 0; MusterScale = 1; MusterLunges = 0;
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
            if (id >= 0) RecordMusterStrike(id, target);
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
            for (var i = formation.Soldiers - 1; i >= 0; i--)
            {
                var art = (i & 1) == 0 ? front : rear;
                var slot = MusterFormationRule.Slot(i, size);
                var stagger = i * .025f;
                var reach = attack >= 0 ? lunge * Mathf.Clamp01(1 - stagger / MusterLungeSeconds) : Vector2.zero;
                DrawActor(art, layer + (slot.y < 0 ? 1 : 0), position + slot + reach, time + i * .07f, art.WorldSize * formation.Scale, walking,
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
