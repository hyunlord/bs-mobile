using System;
using System.Globalization;

namespace Game.App
{
    public readonly struct CaptureSegmentWindow
    {
        public int StartSeconds { get; }
        public int DurationSeconds { get; }
        public bool Landscape { get; }
        public int EndSeconds => StartSeconds + DurationSeconds;
        public CaptureSegmentWindow(int startSeconds, int durationSeconds, bool landscape = false)
        {
            if (startSeconds < 0 || startSeconds > 840) throw new ArgumentOutOfRangeException(nameof(startSeconds));
            if (durationSeconds < 5 || durationSeconds > 60) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (landscape && startSeconds == 0) throw new ArgumentException("--capture-landscape requires a later capture segment.");
            StartSeconds = startSeconds; DurationSeconds = durationSeconds; Landscape = landscape;
        }
        public static CaptureSegmentWindow Parse(string[] args) => new CaptureSegmentWindow(
            Read(args, "--capture-start-seconds", 0), Read(args, "--capture-duration-seconds", 60), Array.IndexOf(args, "--capture-landscape") >= 0);
        static int Read(string[] args, string flag, int fallback)
        {
            var found = false; var result = fallback;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] != flag) continue;
                if (found || i + 1 >= args.Length || !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out result))
                    throw new ArgumentException(flag + " requires one nonnegative integer value.");
                found = true;
            }
            return result;
        }
        public bool Ready(int tick, int tickRate, bool completed)
        {
            if (tick < 0 || tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tick));
            if (completed && tick < EndSeconds * tickRate) throw new InvalidOperationException("Normal run ended before the requested capture segment completed.");
            return tick >= StartSeconds * tickRate;
        }
        public bool Complete(int tick, int tickRate) => tick >= EndSeconds * tickRate;
    }
}
