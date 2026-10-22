using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        // #345 (user decision 2026-10-22): the collection hint ("수확/완료 +N 경험치", "익음", "수리 중") follows the damage numbers'
        // hero keep-out (WorldDamageNumbers.HeroKeepOutOfFigureHeight, 0.6 x the drawn knight's height round its centre). A hint
        // whose box touches that circle moves to the first free spot above the head (clear of the circle and of the visible damage
        // numbers); with none it stays at its usual spot, drawn at WorldDamageNumbers.HeroFadedAlpha.
        static readonly int[] GrowthHintColumns = { 0, 1, -1, 2, -2 };
        const int GrowthHintRows = 3;
        const float GrowthHintStep = .18f, GrowthHintGap = .025f;
        Vector2 heroKeepOutCentre; float heroKeepOutRadius;
        // Comparison switch for tests and review stills; on in every profile.
        public bool GrowthHintKeepOutEnabled { get; set; } = true;
        public int GrowthHintsMovedAboveHero { get; private set; }
        public int GrowthHintsFadedOverHero { get; private set; }

#if UNITY_EDITOR
        public void EditorEnqueueGrowthHintForTesting(WorldHintKind kind, long amount) => growthHints.Enqueue(kind, amount);
        public (bool shown, Bounds bounds, Vector2 centre, float radius) EditorGrowthHintForTesting() =>
            (growthLabels.SlotBounds(0).size.sqrMagnitude > 0 && growthLabels.Shown(0), growthLabels.SlotBounds(0), heroKeepOutCentre, heroKeepOutRadius);
#endif
        void ShowGrowthHint(string hint, Vector2 lordPoint)
        {
            var screen = OverlayWorldBounds();
            var usual = lordPoint + Vector2.up * GameVisualTokens.WaveCollectionLabelRise;
            growthLabels.ShowClamped(0, hint, usual, screen);
            if (!GrowthHintKeepOutEnabled || !WorldGrowthLabels.TouchesCircle(growthLabels.SlotBounds(0), heroKeepOutCentre, heroKeepOutRadius)) return;
            var above = new Vector2(lordPoint.x, heroKeepOutCentre.y + heroKeepOutRadius + growthLabels.SlotBounds(0).extents.y + GrowthHintGap);
            for (var row = 0; row < GrowthHintRows; row++)
                for (var column = 0; column < GrowthHintColumns.Length; column++)
                {
                    growthLabels.ShowClamped(0, hint, above + new Vector2(GrowthHintColumns[column] * GrowthHintStep, row * GrowthHintStep), screen);
                    var placed = growthLabels.SlotBounds(0);
                    if (WorldGrowthLabels.TouchesCircle(placed, heroKeepOutCentre, heroKeepOutRadius)) continue;
                    if ((numbers != null && numbers.OverlapsVisible(placed)) || (lordDamageNumbers != null && lordDamageNumbers.OverlapsVisible(placed))) continue;
                    GrowthHintsMovedAboveHero++;
                    return;
                }
            growthLabels.ShowClamped(0, hint, usual, screen, WorldDamageNumbers.HeroFadedAlpha);
            GrowthHintsFadedOverHero++;
        }
    }
}
