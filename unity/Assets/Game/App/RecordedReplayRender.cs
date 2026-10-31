using System;
using System.Globalization;

namespace Game.App
{
    // Review only: offline-render a bounded window of a replay recorded by an ordinary autoplay run (--capture-save-replay).
    // --wave-benchmark <recorded.ssreplay> --offline-replay-capture --recorded-replay-window <startTick> <endTick>
    // The replay must have been recorded with the current data tree. Commands before the window are applied without
    // rendering, the window renders at the fixed offline 60 fps, and the rest of the original commands run without rendering
    // to the terminal state, which must equal both the recorded terminal and a separate Core replay of the same file.
    // Without the flag nothing changes: the pinned C05, crowd and review replays keep their exact checks.
    public sealed class RecordedReplayRender
    {
        public const int MaximumWindowTicks = 2400;   // 80 s at 30 Hz: a 60 s clip plus settling time
        public int StartTick { get; }
        public int EndTick { get; }
        RecordedReplayRender(int start, int end) { StartTick = start; EndTick = end; }
        public static RecordedReplayRender Parse(string[] args)
        {
            var index = Array.IndexOf(args, "--recorded-replay-window");
            if (index < 0) return null;
            if (index != Array.LastIndexOf(args, "--recorded-replay-window") || index + 2 >= args.Length ||
                !int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                !int.TryParse(args[index + 2], NumberStyles.None, CultureInfo.InvariantCulture, out var end) || end <= start || end - start > MaximumWindowTicks)
                throw new ArgumentException("--recorded-replay-window requires ordered ticks at most " + MaximumWindowTicks + " apart.");
            if (Array.IndexOf(args, "--offline-replay-capture") < 0 || Array.IndexOf(args, "--wave-benchmark") < 0)
                throw new ArgumentException("A recorded replay window requires --wave-benchmark and --offline-replay-capture.");
            foreach (var other in new[] { "--crowd-replay-benchmark", "--m1-combat-case", "--knight-review", "--camera-scale", "--camera-scale-window", "--hero-motion-window", "--wave-benchmark-mode" })
                if (Array.IndexOf(args, other) >= 0) throw new ArgumentException("A recorded replay window cannot be combined with " + other + ".");
            return new RecordedReplayRender(start, end);
        }
    }
}
