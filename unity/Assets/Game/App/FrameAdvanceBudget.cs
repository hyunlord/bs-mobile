namespace Game.App
{
    public struct FrameAdvanceBudget
    {
        public const string DeferredPolicy = "one-advance-per-render-frame-debt-preserved-v1";
        public const string LegacyPolicy = "legacy-unbounded-catch-up";
        int remaining;
        public FrameAdvanceBudget(bool limited) { remaining = limited ? 1 : int.MaxValue; }
        public bool TryAdvance(ref double debt, double step)
        {
            if (remaining == 0 || debt < step) return false;
            debt -= step;
            remaining--;
            return true;
        }
    }
}
