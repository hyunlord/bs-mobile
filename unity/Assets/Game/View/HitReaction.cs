namespace Game.View
{
    // The common enemy hit reaction in one place (user spec 2026-10-19, after the six weapon videos were rejected): every value the
    // hit flash, knockback, death and big-hit stop use. View only; Core, its events and the replay hashes are untouched.
    public static class HitReaction
    {
        public const float Frame = 1f / 60;
        // White flash on every hit, drawn the existing way (flash toward white, outline kept). Two 60Hz frames.
        public const int FlashFrames = 2;
        // The struck enemy is pushed along the hit direction by this many screen pixels (spec 2-4) and eases back.
        public const float KnockbackPixels = 3;
        public const float KnockbackRecoverSeconds = .18f;
        // Death: the flash frames, then the body swells by DeathPopScale and fades out; gone after DeathFrames (spec 6-8).
        public const int DeathFrames = 7;
        public const float DeathPopScale = .3f;
        // Display-only hitstop for big hits, evolution attacks only (Core has no critical hits); spec 30-50 ms.
        public const float BigHitStopSeconds = .04f;
        // At most one big-hit stop per this interval, so a stream of evolved hits (orbit, patch pulses) does not stutter the screen.
        public const float BigHitStopMinIntervalSeconds = .5f;
        public const float FlashSeconds = FlashFrames * Frame;
        public const float DeathSeconds = DeathFrames * Frame;
    }
}
