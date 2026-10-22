using System;
using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class RecordedReplayRenderTests
    {
        // Review-only options are off unless asked for: the pinned C05, crowd and review benchmark arguments and an ordinary
        // autoplay capture parse exactly as before (no recorded window, no saved replay), so their output does not change.
        [Test] public void DefaultBenchmarkAndCaptureArgumentsLeaveBothOptionsOff()
        {
            foreach (var args in new[]
            {
                new[] { "--wave-benchmark", "run.ssreplay", "--wave-benchmark-output", "out" },
                new[] { "--wave-benchmark", "run.ssreplay", "--wave-benchmark-output", "out", "--offline-replay-capture", "--offline-replay-audio" },
                new[] { "--wave-benchmark", "fixture", "--crowd-replay-benchmark" },
            })
                Assert.That(RecordedReplayRender.Parse(args), Is.Null);
            Assert.That(AutoplayCapture.SaveReplayRequested(new[] { "--autoplay-capture", "--capture-output", "out", "--capture-seed", "30000" }), Is.False);
            Assert.That(AutoplayCapture.SaveReplayRequested(new[] { "--autoplay-capture", "--capture-output", "out", "--capture-save-replay" }), Is.True);
        }

        [Test] public void RecordedWindowIsBoundedOfflineAndAlone()
        {
            var window = RecordedReplayRender.Parse(new[] { "--wave-benchmark", "recorded.ssreplay", "--offline-replay-capture", "--recorded-replay-window", "16170", "18000" });
            Assert.That((window.StartTick, window.EndTick), Is.EqualTo((16170, 18000)));
            Assert.That(OfflineReplayCapture.Requested(new[] { "--wave-benchmark", "recorded.ssreplay", "--offline-replay-capture", "--recorded-replay-window", "16170", "18000" }), Is.True, "offline capture accepts the recorded window");
            Assert.Throws<ArgumentException>(() => RecordedReplayRender.Parse(new[] { "--wave-benchmark", "r", "--offline-replay-capture", "--recorded-replay-window", "18000", "16170" }));
            Assert.Throws<ArgumentException>(() => RecordedReplayRender.Parse(new[] { "--wave-benchmark", "r", "--offline-replay-capture", "--recorded-replay-window", "0", "2401" }));
            Assert.Throws<ArgumentException>(() => RecordedReplayRender.Parse(new[] { "--wave-benchmark", "r", "--recorded-replay-window", "0", "600" }), "offline only");
            Assert.Throws<ArgumentException>(() => RecordedReplayRender.Parse(new[] { "--wave-benchmark", "r", "--offline-replay-capture", "--crowd-replay-benchmark", "--recorded-replay-window", "0", "600" }));
        }
    }
}
