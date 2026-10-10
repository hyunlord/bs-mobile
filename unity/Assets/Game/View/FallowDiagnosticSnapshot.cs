namespace Game.View
{
    public struct FallowDiagnosticSnapshot
    {
        public bool Active, ChapterEnabled, MaskBound, ShaderSupported;
        public string ArtProfile, Chapter;
        public int Pixels, NonzeroPixels, GreenPixels, GoldPixels, BuildingPixels;
        public int TotalWork, Seed, Sprout, Ripe, Building, Built, OtherWork, DeadWork;
    }
}
