#if UNITY_EDITOR
using System;
using System.IO;
using System.Globalization;
using Game.App;
using NUnit.Framework;
namespace Game.Tests
{
    public sealed class DevelopmentLongFrameRecorderTests
    {
        static DevelopmentLongFrameRecorder.FrameSample Frame(int frame, double wall = 16) => new DevelopmentLongFrameRecorder.FrameSample { Frame = frame, ObservedFrame = frame + 1, Tick = frame * 2, WallMs = wall, StartedTimestamp = frame * 100, EndedTimestamp = frame * 100 + 90, ObservedTimestamp = frame * 100 + 100, Width = 1920, Height = 1080, Focused = true };
        [Test] public void BenchmarkRepeatAndTraceAreExplicitBoundedCaptureFreeDiagnostics()
        {
            var parsed = RunCoordinator.ParseBenchmarkDiagnosticOptions(new[] { "--wave-benchmark", "fixture", "--wave-benchmark-repeat", "2", "--wave-benchmark-profile", "--wave-benchmark-trace-range", "7900", "8000" });
            Assert.That(parsed.Passes, Is.EqualTo(2));
            Assert.That(parsed.TraceRange, Is.EqualTo(new[] { 7900, 8000 }));
            Assert.That(RunCoordinator.ParseBenchmarkDiagnosticOptions(Array.Empty<string>()).Passes, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseBenchmarkDiagnosticOptions(new[] { "--wave-benchmark-repeat", "2" }));
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseBenchmarkDiagnosticOptions(new[] { "--wave-benchmark", "fixture", "--wave-benchmark-repeat", "3" }));
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseBenchmarkDiagnosticOptions(new[] { "--wave-benchmark", "fixture", "--wave-benchmark-repeat", "2", "--wave-benchmark-capture" }));
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseBenchmarkDiagnosticOptions(new[] { "--wave-benchmark", "fixture", "--wave-benchmark-trace-range", "7900", "8000" }));
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseBenchmarkDiagnosticOptions(new[] { "--wave-benchmark", "fixture", "--wave-benchmark-profile", "--wave-benchmark-trace-range", "7900", "8200" }));
        }
        [Test] public void RegistrationRefreshKeepsHistoricalUnavailableMetadataAndRegistersLateMarker()
        {
            var name = "SowSiege.LateMarkerTest." + Guid.NewGuid().ToString("N");
            var directory = Path.Combine(Path.GetTempPath(), "sowsiege-late-marker-" + Guid.NewGuid().ToString("N"));
            using var recorder = DevelopmentLongFrameRecorder.WithDeferredMarkerForTesting(name);
            recorder.Observe(Frame(1, 16));
            var marker = new Unity.Profiling.ProfilerMarker(name);
            using (marker.Auto()) { }
            recorder.RefreshRegistrationAfterLoading();
            recorder.Observe(Frame(2, 26));
            try
            {
                recorder.Write(directory);
                var lines = File.ReadAllLines(Path.Combine(directory, "counter-observations.csv"));
                Assert.That(lines.Length, Is.EqualTo(3));
                Assert.That(lines[1], Does.StartWith("1,2,200,"));
                Assert.That(lines[1], Does.Contain(",\"unavailable\",-1,\"not-registered-at-start\","));
                Assert.That(lines[2], Does.StartWith("2,3,300,"));
                Assert.That(lines[2], Does.Contain("\"registered-after-loading\""));
                Assert.That(lines[2], Does.Not.Contain("\"unavailable\""));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        [Test] public void ExplicitOptInRejectsDuplicates()
        {
            Assert.That(DevelopmentLongFrameRecorder.Requested(Array.Empty<string>()), Is.False);
            Assert.That(DevelopmentLongFrameRecorder.Requested(new[] { "--diagnostic-long-frames" }), Is.True);
            Assert.Throws<ArgumentException>(() => DevelopmentLongFrameRecorder.Requested(new[] { "--diagnostic-long-frames", "--diagnostic-long-frames" }));
        }
        [Test] public void RingPreservesFourNeighborsAndDoesNotDuplicateOverlappingWindows()
        {
            using var recorder = new DevelopmentLongFrameRecorder(32, false);
            for (var i = 0; i <= 14; i++) recorder.Observe(Frame(i, i == 7 || i == 9 ? 26 : 16));
            Assert.That(recorder.LongFrames, Is.EqualTo(2)); Assert.That(recorder.SavedFrames, Is.EqualTo(11));
            var writer = new StringWriter(); recorder.WriteFramesCsv(writer, false);
            var lines = writer.ToString().Trim().Split('\n');
            Assert.That(lines[1], Does.StartWith("3,4,")); Assert.That(lines[11], Does.StartWith("13,14,"));
            writer = new StringWriter(); recorder.WriteFramesCsv(writer, true);
            Assert.That(writer.ToString().Trim().Split('\n').Length, Is.EqualTo(3));
        }
        [Test] public void ExactThresholdIsNotLongAndOverflowIsExplicit()
        {
            using var recorder = new DevelopmentLongFrameRecorder(5, false);
            recorder.Observe(Frame(0, 25)); Assert.That(recorder.LongFrames, Is.Zero);
            for (var i = 1; i < 12; i++) recorder.Observe(Frame(i, 26));
            Assert.That(recorder.Overflowed, Is.True); Assert.That(recorder.LongFrames, Is.EqualTo(11)); Assert.That(recorder.SavedFrames, Is.EqualTo(5));
        }
        [Test] public void EventCapacityIsIndependentAndOverflowIsExplicit()
        {
            using var recorder = new DevelopmentLongFrameRecorder(5, false, 2);
            recorder.RecordEvent(DevelopmentLongFrameRecorder.EventFlags.Card, 1, 1, 1, 1);
            recorder.RecordEvent(DevelopmentLongFrameRecorder.EventFlags.Card, 2, 2, 1, 1);
            Assert.That(recorder.Overflowed, Is.False);
            recorder.RecordEvent(DevelopmentLongFrameRecorder.EventFlags.Card, 3, 3, 1, 1);
            Assert.That(recorder.Overflowed, Is.True);
            var writer = new StringWriter(); recorder.WriteEventsCsv(writer);
            Assert.That(writer.ToString().Trim().Split('\n').Length, Is.EqualTo(3));
            Assert.That(recorder.SavedFrames, Is.Zero);
        }
        [TestCase(399, "below-400")]
        [TestCase(400, "400-599")]
        [TestCase(599, "400-599")]
        [TestCase(600, "600-799")]
        [TestCase(799, "600-799")]
        [TestCase(800, "800-plus")]
        public void DensityBinUsesExactBoundaries(int enemies, string expected)
        {
            using var recorder = new DevelopmentLongFrameRecorder(5, false, 1);
            var sample = Frame(1, 26); sample.Enemies = enemies; recorder.Observe(sample);
            var writer = new StringWriter(); recorder.WriteFramesCsv(writer, true);
            Assert.That(writer.ToString().Trim(), Does.EndWith("," + expected));
        }
        [Test] public void EventsKeepTheirOwnTimesAndStateChangesAreRecorded()
        {
            using var recorder = new DevelopmentLongFrameRecorder(16, false);
            recorder.Observe(Frame(0));
            recorder.RecordEvent(DevelopmentLongFrameRecorder.EventFlags.Card, 1, 123, 1920, 1080, "core:card,quoted\"id", 15);
            var next = Frame(1, 26); next.Focused = false; next.Paused = true; next.Width = 1280; recorder.Observe(next);
            var writer = new StringWriter(); recorder.WriteEventsCsv(writer);
            Assert.That(writer.ToString(), Does.Contain("1,123,1920,1080,8,callback,\"core:card,quoted\"\"id\",15"));
            Assert.That(writer.ToString(), Does.Contain("1,190,1280,1080,7"));
        }
        [Test] public void SerializationPreservesObservedTimestampAndInvariantDecimals()
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                using var recorder = new DevelopmentLongFrameRecorder(16, false);
                var sample = Frame(3, 25.5); sample.CoreMs = 1.25; sample.ClearMs = 0.125;
                recorder.Observe(sample);
                var writer = new StringWriter(); recorder.WriteFramesCsv(writer, true);
                var lines = writer.ToString().Trim().Split('\n');
                var headers = lines[0].TrimEnd('\r').Split(',');
                var values = lines[1].TrimEnd('\r').Split(',');
                Assert.That(values.Length, Is.EqualTo(headers.Length));
                Assert.That(values[Array.IndexOf(headers, "observedTimestamp")], Is.EqualTo("400"));
                Assert.That(values[Array.IndexOf(headers, "endTimestamp")], Is.EqualTo("390"));
                Assert.That(values[Array.IndexOf(headers, "wallMs")], Is.EqualTo("25.5"));
                Assert.That(values[Array.IndexOf(headers, "coreMs")], Is.EqualTo("1.25"));
                Assert.That(values[Array.IndexOf(headers, "clearMs")], Is.EqualTo("0.125"));
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        }
        [Test] public void MissingFramesAreNotInventedAndInvalidOrderFails()
        {
            using var recorder = new DevelopmentLongFrameRecorder(16, false);
            recorder.Observe(Frame(1)); recorder.Observe(Frame(10, 26));
            Assert.That(recorder.SavedFrames, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => recorder.Observe(Frame(10)));
            var invalid = Frame(11); invalid.ObservedFrame = 11;
            Assert.Throws<ArgumentException>(() => recorder.Observe(invalid));
        }
    }
}
#endif
