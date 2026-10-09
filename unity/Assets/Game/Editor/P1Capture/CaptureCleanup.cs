using System;
using System.Collections.Generic;

namespace Game.P1Capture
{
    public static class CaptureCleanup
    {
        public static Exception Run(params Action[] steps)
        {
            var errors = new List<Exception>();
            foreach (var step in steps)
            {
                try { step(); }
                catch (Exception error) { errors.Add(error); }
            }
            return errors.Count == 0 ? null : new AggregateException("Capture cleanup failed.", errors);
        }
    }
}
