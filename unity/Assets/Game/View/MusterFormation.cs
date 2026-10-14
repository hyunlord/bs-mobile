using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public enum MusterPresentationPath { None, GearRatio, RuntimeEvolution }

    public readonly struct MusterFormation
    {
        public readonly int Soldiers;
        public readonly float Scale, Ratio;
        public readonly MusterPresentationPath Path;
        public readonly string EvolutionId;
        public bool Evolved => Path == MusterPresentationPath.RuntimeEvolution;
        public MusterFormation(int soldiers, float scale, float ratio, MusterPresentationPath path, string evolutionId)
        { Soldiers = soldiers; Scale = scale; Ratio = ratio; Path = path; EvolutionId = evolutionId; }
    }

    // How many and how large muster soldiers are drawn. The reference is the canonical catalog's top level of the same gear,
    // so a canonical max-level horn draws exactly as before. Only an active runtime evolution enables the evolution look.
    public static class MusterFormationRule
    {
        public const int MaximumSoldiers = 5;
        public const float MaximumScale = 1.3f, ScalePerDoubling = .06f;
        // Soldiers keep this many hero body widths from the hero's centre and never overlap the hero's drawn box (P5).
        public const float HeroClearanceBodyWidths = 1.2f, FrontArcDegrees = 150;

        public static void ReferenceMaximum(WaveGearDefinition reference, out int count, out int range)
        {
            count = reference.Count; range = reference.Range;
            if (reference.Levels == null) return;
            for (var i = 0; i < reference.Levels.Length; i++)
            { count = Math.Max(count, reference.Levels[i].Count); range = Math.Max(range, reference.Levels[i].Range); }
        }

        public static void Effective(WaveGearDefinition runtime, int level, out int count, out int range)
        {
            if (runtime.Levels == null || runtime.Levels.Length == 0) { count = runtime.Count; range = runtime.Range; return; }
            var parameters = runtime.Levels[Mathf.Clamp(level, 1, runtime.Levels.Length) - 1];
            count = parameters.Count; range = parameters.Range;
        }

        public static float Ratio(int count, int range, int referenceCount, int referenceRange)
            => Mathf.Max(1, Mathf.Max((float)count / Math.Max(1, referenceCount), (float)range / Math.Max(1, referenceRange)));

        public static float Ratio(WaveGearDefinition runtime, int level, WaveGearDefinition reference)
        {
            Effective(runtime, level, out var count, out var range);
            ReferenceMaximum(reference ?? runtime, out var referenceCount, out var referenceRange);
            return Ratio(count, range, referenceCount, referenceRange);
        }

        public static MusterFormation Resolve(int baseSoldiers, float ratio, string evolutionId)
        {
            baseSoldiers = Mathf.Clamp(baseSoldiers, 1, MaximumSoldiers);
            ratio = Mathf.Max(1, ratio);
            if (!string.IsNullOrEmpty(evolutionId))
                return new MusterFormation(MaximumSoldiers, MaximumScale, ratio, MusterPresentationPath.RuntimeEvolution, evolutionId);
            var doublings = Mathf.Log(ratio, 2);
            var soldiers = Mathf.Clamp(baseSoldiers + Mathf.RoundToInt(doublings), baseSoldiers, MaximumSoldiers);
            var scale = Mathf.Min(MaximumScale, 1 + ScalePerDoubling * doublings);
            return new MusterFormation(soldiers, scale, ratio, ratio > 1 ? MusterPresentationPath.GearRatio : MusterPresentationPath.None, null);
        }

        public static string ActiveWaveEvolution(IReadOnlyList<string> active, IReadOnlyDictionary<string, WaveEvolutionDefinition> definitions, string gearId)
        {
            if (active == null || definitions == null) return null;
            for (var i = 0; i < active.Count; i++)
                if (definitions.TryGetValue(active[i], out var evolution) && Array.IndexOf(evolution.InputIds, gearId) >= 0) return active[i];
            return null;
        }

        // First-playable runtime state: an activated evolution of the soldiers' own source tool (its BaseId), so a co-ingredient's
        // people never borrow the muster evolution look.
        public static string ActiveRuntimeEvolution(IReadOnlyList<EvolutionClueView> clues, IReadOnlyDictionary<string, EvolutionDefinition> definitions, string sourceId)
        {
            if (clues == null || definitions == null || string.IsNullOrEmpty(sourceId)) return null;
            for (var i = 0; i < clues.Count; i++)
                if (clues[i].Activated && definitions.TryGetValue(clues[i].Id, out var evolution) && evolution.BaseId == sourceId) return clues[i].Id;
            return null;
        }

        public static Rect SpriteRect(Vector2 position, Vector2 size, Vector2 pivot) => new Rect(position - Vector2.Scale(size, pivot), size);

        // Moves a soldier outward from the hero along its own bearing until it is clear of the hero's box and minimum distance.
        public static Vector2 ClearOfHero(Vector2 desired, Vector2 size, Vector2 pivot, Rect hero, float minimumDistance, Vector2 fallback)
        {
            var center = hero.center; var offset = desired - center;
            var direction = offset.sqrMagnitude > .000001f ? offset.normalized : fallback.sqrMagnitude > .000001f ? fallback.normalized : Vector2.up;
            var distance = offset.magnitude; var step = Mathf.Max(.005f, minimumDistance * .04f);
            for (var i = 0; i < 512; i++)
            {
                var at = center + direction * distance;
                if (distance >= minimumDistance && !SpriteRect(at, size, pivot).Overlaps(hero)) return at;
                distance = Mathf.Max(distance + step, minimumDistance);
            }
            return center + direction * distance;
        }

        // A formation standing on the hero spreads over the front half-ring facing its target instead of covering the hero.
        public static Vector2 FrontArc(int index, int count, Rect hero, Vector2 front, float radius)
        {
            var heading = Mathf.Atan2(front.y, front.x) * Mathf.Rad2Deg;
            var angle = count <= 1 ? heading : heading + Mathf.Lerp(-FrontArcDegrees * .5f, FrontArcDegrees * .5f, index / (float)(count - 1));
            return hero.center + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
        }

        // Fixed, deterministic ranks behind the representative; index 0 is the representative itself.
        public static Vector2 Slot(int index, Vector2 bodySize)
        {
            if (index <= 0) return Vector2.zero;
            var rank = (index + 1) / 2; var side = (index & 1) == 0 ? 1 : -1;
            return new Vector2(side * bodySize.x * .55f * rank, -bodySize.y * .16f * rank + (rank & 1) * bodySize.y * .05f);
        }
    }
}
