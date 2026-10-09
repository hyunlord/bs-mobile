using Unity.Profiling;

namespace Game.App
{
    internal static class RunProfilerMarkers
    {
        internal static readonly ProfilerMarker CoreApply = new ProfilerMarker("SowSiege.CoreApply");
        internal static readonly ProfilerMarker CaptureRun = new ProfilerMarker("SowSiege.CaptureRun");
        internal static readonly ProfilerMarker CaptureFirstPlayable = new ProfilerMarker("SowSiege.CaptureFirstPlayable");
        internal static readonly ProfilerMarker AcceptPresentation = new ProfilerMarker("SowSiege.AcceptPresentation");
        internal static readonly ProfilerMarker WorldPresent = new ProfilerMarker("SowSiege.WorldPresent");
        internal static readonly ProfilerMarker Hud = new ProfilerMarker("SowSiege.Hud");
        internal static readonly ProfilerMarker Telemetry = new ProfilerMarker("SowSiege.Telemetry");
        internal static readonly ProfilerMarker RecordingWrite = new ProfilerMarker("SowSiege.Recording.Write");
        internal static readonly ProfilerMarker RecordingCheckpoint = new ProfilerMarker("SowSiege.Recording.Checkpoint");
        internal static readonly ProfilerMarker RecordingFinish = new ProfilerMarker("SowSiege.Recording.Finish");
    }
}
