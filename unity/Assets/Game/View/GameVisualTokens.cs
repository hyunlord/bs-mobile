using UnityEngine;

namespace Game.View
{
    // Values are the runtime counterpart of unity/DESIGN.md, sections 2, 4, 6 and 7.
    public static class GameVisualTokens
    {
        public static readonly Color FallowAsh = new Color32(0x79, 0x7B, 0x79, 255);
        public const int FallowMaskSize = 128;
        public const float FallowMaskInterval = .1f, FallowHarvestSeconds = 1.4f;
        public const float FallowSeedRadius = .42f, FallowRipeRadius = .95f, FallowBuildingRadius = 1.3f;
        public const float FallowSeedWidth = .28f, FallowSproutWidth = .34f, FallowRipeWidth = .40f, FallowWorkshopScale = 1f;
        public static readonly Vector2 FallowLordViewportAnchor = new Vector2(.60f, .57f);
        public const float FallowPresentationZoom = 1.15f;
        public const float OrbitRenderLag = .10f, ActorAttackSeconds = .22f;
        public const float ActorTurnSeconds = .10f, ActorHeadingLean = 3f;
        public static readonly Color Ink = new Color32(0x29, 0x28, 0x22, 255);
        public static readonly Color Ally = new Color32(0x37, 0x64, 0x8B, 255);
        public static readonly Color Ready = new Color32(0xEB, 0xC5, 0x62, 255);
        public static readonly Color Attack = new Color32(0xF8, 0xEB, 0xC7, 255);
        public static readonly Color Hostile = new Color32(0xB6, 0x47, 0x38, 255);
        public static readonly Color[] Seasons = { new Color32(0x8B, 0x98, 0x74, 255), new Color32(0x77, 0x8B, 0x62, 255), new Color32(0xA4, 0x9A, 0x68, 255), new Color32(0xCB, 0xD0, 0xC1, 255) };
        public static readonly string[] SeasonNames = { "spring", "summer", "autumn", "winter" };
        public const int TerrainLayer = 0, GrowthLayer = 10, ExperienceLayer = 15, ReadyLayer = 20, AllyLayer = 30, AttackLayer = 40, EnemyLayer = 50, LordLayer = 80, ThreatLayer = 90;
        public const float SeasonBlendSeconds = 1.8f, HitFlashSeconds = 0.09f, KillSeconds = 0.32f, HarvestSeconds = 0.48f, ExperienceSeconds = 0.42f, EmphasisSeconds = 0.45f;
        public const float CameraOutsideMargin = 1f, CameraReferencePixelHeight = 1600f;
        public const float TerrainUvInset = 0.16f, TerrainStride = 1f, TerrainOpacity = 0.45f;
        public const float FieldOpacity = 0.10f, AreaAttackOpacity = 0.26f, TravellingAttackOpacity = 0.72f, AttackRibbonWidth = 0.08f;
        public const float WaveHostileTellOpacity = .55f, WaveHostileActiveOpacity = .8f;
        public const float WaveFragmentScale = .32f, WaveWorkshopScale = .48f, WavePoolScale = .48f, WavePoolOpacity = .65f;
        public const float WaveStatusScale = .36f, WaveWetHeight = .55f, WaveWetOpacity = .38f;
        public const float WaveTimberSourceScale = .42f, WaveRewardScale = .18f;
        public const float WavePathSpacingFraction = .8f, WaveEdgeActorMargin = .8f;
        public const float WaveCropSpacingFraction = .8f, WaveCropOpacity = .8f, WaveTerrainOpacity = .18f, WaveHeroOutline = .045f, WaveFenceOpacity = .65f;
        public const int WaveAttackEdgeLayer = 70, WaveDangerLayer = 72;
        public const float WaveEdgeTexels = 2f, WaveEdgePixels = 1.25f, WaveEdgeOpacity = .72f, WavePlantingInnerScale = .78f;
        public const float WaveEnemyMaxSize = .64f, WaveWaspScale = .55f, WaveEnemyUpperOpacity = .55f;
        public const float WaveLordRingRadius = .34f, WaveLordRingHeight = .55f, WaveLordRingInner = .82f, WaveLordRingAccentScale = .94f, WaveLordRingAccentInner = .9f;
        public const float WaveRewardOpacity = .55f, WaveRipeEdgeOpacity = .55f;
        public const float WaveAttackBodyOpacity = .78f, WaveChainRibbonWidth = .18f, WaveRepairBannerOpacity = .48f;
        public const int WaveReadinessCueLayer = 69, WaveTransientCueLayer = 71;
        public const float WaveChainOutlinePixels = 1.25f, WaveRipeCueRadius = 1.2f, WaveRipeCueScale = .5f, WaveRipeCueRise = .25f;
        public const float WaveIntakeScale = .5f, WaveIntakeRise = .3f, WaveRepairShieldScale = .25f;
        public const int WaveHarvestCueLimit = 3;
        public const float WaveGrowthLabelRise = .5f, WaveCollectionLabelRise = .9f, WaveCollectionLabelSeconds = .9f;
        public const float GroupRepresentativeOffset = .22f, StockBundleScale = .28f;
        public const float WalkSeconds = 0.36f, WalkBob = 0.018f, WalkLeanDegrees = 3, DamageNumberSeconds = 0.65f, ShakeSeconds = 0.18f, ShakeAmplitude = 0.035f;
    }
}
