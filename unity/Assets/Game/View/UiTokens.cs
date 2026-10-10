using UnityEngine;
namespace Game.View
{
    public static class UiTokens
    {
        public const float GrowthStartScale = .82f;
        public const int FallowTitleLogo = 88, FallowEvolution = 32;
        public const float StickThumbDiameter = 80;
        public const float FallowHudHeight = 104, FallowFooterHeight = 156, FallowSlotSize = 76;
        public static readonly Color FallowPaperShade = new Color32(0xC9, 0xB8, 0x87, 255);
        public static readonly Color FallowMuted = new Color32(0x77, 0x6F, 0x59, 255);
        public static readonly Color FallowCharcoal = new Color32(0x2E, 0x32, 0x35, 255);
        public const float FallowFrameMultiplier = 5;
        public const int Display = 48, Title = 36, Heading = 30, Body = 26, Small = 22, Caption = 20;
        public const float MinTouchHeight = 88, BottomWorldInset = 180, PanelInset = 48, CardInset = 36;
        public const float FrameBorderFraction = 0.2f, FramePixelsPerUnitMultiplier = 2;
        public const float Gap = 16, Padding = 24, TouchHeight = 88, IconSize = 64, StickRadius = 120;
    }
}
