using System;
using System.Collections.Generic;

namespace Game.App
{
    public static class CaptureCompletion
    {
        public static void Run(bool success, Action writeLedger, Action cleanup, Action<bool, Exception> writeResult, Action<int> exit)
        {
            var errors = new List<Exception>();
            void Attempt(Action step)
            {
                try { step(); }
                catch (Exception error) { success = false; errors.Add(error); }
            }
            Exception Failure() => errors.Count == 0 ? null : new AggregateException("Capture finalization failed.", errors);
            try
            {
                Attempt(writeLedger);
                Attempt(cleanup);
                var errorsBeforeResult = errors.Count;
                Attempt(() => writeResult(success, Failure()));
                if (errors.Count != errorsBeforeResult) Attempt(() => writeResult(false, Failure()));
            }
            finally { exit(success ? 0 : 1); }
        }
    }

    public static class CaptureRestartGate
    {
        public static bool IsReady(double elapsedSeconds, string previousReplay, string currentReplay, bool running, int tick)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (elapsedSeconds >= 30) throw new TimeoutException("Restart did not advance a new normal run within 30 real seconds.");
            return running && tick > 0 && !string.IsNullOrEmpty(currentReplay) && currentReplay != previousReplay;
        }
    }
}
