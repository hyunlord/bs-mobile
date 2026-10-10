using System;
using Game.App;
using Game.Editor;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class DevelopmentProfilerTraceTests
    {
        [Test] public void NormalTraceStillRequiresTelemetryWhileBenchmarkRequiresItsSnapshotAndAcceptScopes()
        {
            Assert.That(ProfilerTraceExport.RequiredMarkers(DevelopmentProfilerTrace.NormalScope), Is.EquivalentTo(new[]
            {
                "SowSiege.CoreApply", "SowSiege.CaptureRun", "SowSiege.CaptureFirstPlayable", "SowSiege.WorldPresent", "SowSiege.Hud", "SowSiege.Telemetry"
            }));
            var benchmark = ProfilerTraceExport.RequiredMarkers(DevelopmentProfilerTrace.BenchmarkScope);
            Assert.That(benchmark, Does.Contain("SowSiege.BenchmarkSnapshot"));
            Assert.That(benchmark, Does.Contain("SowSiege.AcceptPresentation"));
            Assert.That(benchmark, Does.Not.Contain("SowSiege.Telemetry"));
            Assert.Throws<ArgumentException>(() => ProfilerTraceExport.RequiredMarkers("unknown"));
        }
        [Test] public void BenchmarkModeIsExplicitAndDefaultsToCapped()
        {
            Assert.That(RunCoordinator.ParseWaveBenchmarkMode(Array.Empty<string>()), Is.EqualTo("capped"));
            Assert.That(RunCoordinator.ParseWaveBenchmarkMode(new[] { "--wave-benchmark-mode", "uncapped" }), Is.EqualTo("uncapped"));
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseWaveBenchmarkMode(new[] { "--wave-benchmark-mode" }));
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseWaveBenchmarkMode(new[] { "--wave-benchmark-mode", "fast" }));
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseWaveBenchmarkMode(new[] { "--wave-benchmark-mode", "capped", "--wave-benchmark-mode", "uncapped" }));
        }
        [Test] public void BenchmarkProfilerRequiresDevelopmentAndOneExplicitFlag()
        {
            Assert.That(RunCoordinator.ParseWaveBenchmarkProfile(Array.Empty<string>(), false), Is.False);
            Assert.That(RunCoordinator.ParseWaveBenchmarkProfile(new[] { "--wave-benchmark-profile" }, true), Is.True);
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseWaveBenchmarkProfile(new[] { "--wave-benchmark-profile" }, false));
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseWaveBenchmarkProfile(new[] { "--wave-benchmark-profile", "--wave-benchmark-profile" }, true));
        }
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
