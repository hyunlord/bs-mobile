using System;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    // When a weapon shows its evolution-only element (storm sky, muster flag bearer). Continuous size/count scaling uses
    // max(count, range) over the canonical top level; the evolution element alone gates on reach (range) because counts
    // differ by orders of magnitude between weapons (muster review max has 8x count at only 3.1x reach).
    public static class WeaponEvolutionGrade
    {
        // Range ratio against the canonical maximum level of the same gear. Canonical non-evolved play is <= 1; the review
        // hypotheses sit at storm 1.56/3.12/5.85 and muster 1.88/3.14/4.71 (lv1/max/evolved). Not data: changing data/ would
        // change the content hash that replays and review fixtures are bound to, so this stays a presentation constant.
        public const float EvolutionRangeRatio = 4f;

        public static float RangeRatio(WaveGearDefinition runtime, int level, WaveGearDefinition reference)
        {
            MusterFormationRule.Effective(runtime, level, out _, out var range);
            MusterFormationRule.ReferenceMaximum(reference ?? runtime, out _, out var referenceRange);
            return Mathf.Max(1, (float)range / Math.Max(1, referenceRange));
        }

        public static bool Active(float rangeRatio, string runtimeEvolution)
            => !string.IsNullOrEmpty(runtimeEvolution) || rangeRatio >= EvolutionRangeRatio;
    }
}
