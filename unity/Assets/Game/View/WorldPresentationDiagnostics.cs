namespace Game.View
{
    public struct WorldPresentationDiagnostics
    {
        public double TotalMs;
        public double SetupMs;
        public double CameraUiMs;
        public double FallowMaskMs;
        public double TerrainLandmarksMs;
        // DrawWave owns both wave work and wave attacks; this stage preserves that call boundary.
        public double WaveWorldObjectsMs;
        public double AttackEffectsMs;
        public double EnemyActorsMs;
        public double HeroThreatsMs;
        public double SpriteBatchSubmitMs;
        public double CleanupMs;
        public int DrawCalls;
        public int SubmittedInstances;
        public int MaskUpdates;
        public int MaskPixelsUploaded;
        public int MaskPaintPixelVisits;
        public int GroundingAdds;
    }
}
