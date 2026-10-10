using System;
using Game.App;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class DevelopmentProfilerTraceTests
    {
        [Test] public void MissingOptionNeverArmsAndCrossingTickConsumesOnce()
        {
            Assert.That(new DevelopmentProfilerTrace(Array.Empty<string>()).TryConsumeScheduledTick(30000, true), Is.False);
            var trace = new DevelopmentProfilerTrace(new[] { "app", "--diagnostic-trace-tick", "20250" });
            Assert.That(trace.TryConsumeScheduledTick(20249, true), Is.False);
            Assert.That(trace.TryConsumeScheduledTick(20250, false), Is.False);
            Assert.That(trace.TryConsumeScheduledTick(20251, true), Is.True);
            Assert.That(trace.TryConsumeScheduledTick(20252, true), Is.False);
            Assert.That(trace.TryConsumeScheduledTick(20251, true), Is.False);
        }
        [TestCase("-1")][TestCase("NaN")][TestCase("2147483648")][TestCase("")]
        public void InvalidTickFailsClosed(string value)
            => Assert.Throws<ArgumentException>(() => DevelopmentProfilerTrace.ParseRequestedTick(new[] { "--diagnostic-trace-tick", value }));
        [Test] public void MissingValueAndDuplicateOptionFailClosed()
        {
            Assert.Throws<ArgumentException>(() => DevelopmentProfilerTrace.ParseRequestedTick(new[] { "--diagnostic-trace-tick" }));
            Assert.Throws<ArgumentException>(() => DevelopmentProfilerTrace.ParseRequestedTick(new[] { "--diagnostic-trace-tick", "0", "--diagnostic-trace-tick", "1" }));
            Assert.That(DevelopmentProfilerTrace.ParseRequestedTick(new[] { "--diagnostic-trace-tick", "0" }), Is.Zero);
        }
    }
}
