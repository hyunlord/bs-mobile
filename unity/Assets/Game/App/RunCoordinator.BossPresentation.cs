using Game.View;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        BossPresentationTimeline bossPresentation;
        WorldRenderer bossPresentationWorld;
        bool initializingBossPresentation;
        System.Action<string> commonWaveFeedback;
        readonly HeroPresentationDelay heroPresentationDelay = new HeroPresentationDelay();

        void AcceptCurrentWorldPresentation()
        {
            if (bossPresentation != null) return;
            world?.AcceptWave(runCatalog, CurrentWave);
            world?.AcceptFrame(CurrentFrame, FirstPlayable);
        }

        void DeliverBossPresentation(BossPresentationTimeline.CaptureSlot slot)
        {
            if (world == null || !initializingBossPresentation && !ReferenceEquals(bossPresentationWorld, world) ||
                initializingBossPresentation && !ReferenceEquals(slot.Frame, CurrentFrame)) return;
            world.AcceptWave(runCatalog, slot.Wave);
            world.AcceptFrame(slot.Frame, slot.FirstPlayable);
            if(waveSound!=null)sound?.AcceptCommonFeedback(slot.Frame,commonWaveFeedback??=waveSound.PlayCommon);
            AcceptWaveAudio(slot.Frame,slot.Wave);
        }

        void PresentWorld(float alpha, float seconds, Rect visible, bool terminalPresentation = false)
        {
            world.PresentationPaused = !waveBenchmarkRequested && Paused;
            if (bossPresentation == null)
            {
                world.Present(previous, CurrentFrame, FirstPlayable, alpha, seconds, visible);
                return;
            }
            if (!ReferenceEquals(bossPresentationWorld, world))
            {
                heroPresentationDelay.Reset();
                // Preroll never replays old effects into the first visible frame.
                initializingBossPresentation = true;
                try { bossPresentation.Present(1, 0); }
                finally { initializingBossPresentation = false; }
                bossPresentationWorld = world;
                world.BossHitStopRequested = bossPresentation.RequestHitStop;
                world.HitStopRequested = bossPresentation.RequestHitStop;
                world.BossDeathSlowRequested = bossPresentation.RequestDeathSlow;
            }
            if (!world.ImpactFeedbackEnabled) bossPresentation.CancelFeedback();
            var sample = bossPresentation.Present(alpha, seconds);
            world.AcceptWave(runCatalog,sample.Wave,false);
            var prediction = world.PredictedLordPosition;
            var interpolated = Vector2.Lerp(PresentationPoint(sample.Previous.Lord.Position),
                PresentationPoint(sample.Current.Lord.Position), sample.Alpha);
            world.PredictedLordPosition = heroPresentationDelay.Resolve(interpolated, prediction, sample.Delayed, seconds);
            try
            {
                world.Present(sample.Previous, sample.Current, sample.FirstPlayable, sample.Alpha,
                    sample.PresentationDelta(seconds, terminalPresentation ? CurrentFrame.Tick : -1), visible, false);
                heroPresentationDelay.CommitRendered(world.RenderedLordPosition);
            }
            finally { world.PredictedLordPosition = prediction; }
        }
    }
}
