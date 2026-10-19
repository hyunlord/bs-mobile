#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
        [Test] public void RequiredDiagnosticCountersIncludeViewFirstUseMarkerFamilies()
        {
            var names = DevelopmentLongFrameRecorder.RequiredCounterNamesForTesting();
            Assert.That(names, Does.Contain("SowSiege.View.SpriteBatchMeshUpload"));
            Assert.That(names, Does.Contain("SowSiege.View.SpriteBatchPropertyUpload"));
            Assert.That(names, Does.Contain("SowSiege.View.SpriteBatchCapacityFlush"));
            Assert.That(names, Does.Contain("SowSiege.View.SpriteBatchDraw"));
            Assert.That(names, Does.Contain("SowSiege.View.AudioResourceLoad"));
            Assert.That(names, Does.Contain("SowSiege.View.AudioSourceCreate"));
            Assert.That(names, Does.Contain("SowSiege.View.AudioPlayback"));
            Assert.That(names, Does.Contain("SowSiege.View.AudioFirstPlayback"));
            Assert.That(names, Does.Contain("SowSiege.View.FontSupportProbe"));
            Assert.That(names, Does.Contain("SowSiege.View.FontRequestCharacters"));
            Assert.That(names, Does.Contain("SowSiege.View.FontNewCharacters"));
            Assert.That(names, Does.Contain("SowSiege.View.FontTextureRebuilt"));
            Assert.That(new HashSet<string>(names).Count, Is.EqualTo(names.Length));
        }
        [Test] public void ExplicitOptInRejectsDuplicates()
        {
            Assert.That(DevelopmentLongFrameRecorder.Requested(Array.Empty<string>()), Is.False);
            Assert.That(DevelopmentLongFrameRecorder.Requested(new[] { "--diagnostic-long-frames" }), Is.True);
            Assert.Throws<ArgumentException>(() => DevelopmentLongFrameRecorder.Requested(new[] { "--diagnostic-long-frames", "--diagnostic-long-frames" }));
        }
        [Test] public void CapacityEvidencePreservesGrowthCoordinatesAndReportsOverflow()
        {
            using var recorder = DevelopmentLongFrameRecorder.WithSmallCapacityGrowthBufferForTesting();
            recorder.RecordCapacityGrowth(10, 20, 300, "enemies", 64, 64, 40);
            recorder.RecordCapacityGrowth(11, 21, 301, "enemies", 64, 128, 65);
            recorder.RecordCapacityGrowth(12, 22, 302, "effects", 16, 32, 17);
            Assert.That(recorder.Overflowed, Is.False);
            recorder.RecordCapacityGrowth(13, 23, 303, "effects", 32, 64, 33);
            Assert.That(recorder.Overflowed, Is.True);
            var writer = new StringWriter(); recorder.WriteCapacityGrowthCsv(writer);
            var rows = writer.ToString().Trim().Split('\n');
            Assert.That(rows.Length, Is.EqualTo(3));
            Assert.That(rows[1].Trim(), Is.EqualTo("11,21,301,\"enemies\",64,128,65,growth"));
            Assert.That(rows[2].Trim(), Is.EqualTo("12,22,302,\"effects\",16,32,17,growth"));
        }
        [Test] public void MemoryLedgerSamplesAtMostOncePerSecondAndDoesNotInventCatchUpRows()
        {
            using var recorder = new DevelopmentLongFrameRecorder(8, false);
            var frame = Frame(1); frame.StartedTimestamp = 100; frame.Tick = 300; frame.RuntimeSeconds = 10; frame.Enemies = 411;
            recorder.RecordMemoryForTesting(frame, 11, 22, 33, 44);
            frame.StartedTimestamp += System.Diagnostics.Stopwatch.Frequency - 1;
            recorder.RecordMemoryForTesting(frame, 99, 99, 99, 99);
            frame.StartedTimestamp++; frame.Frame = 2; frame.Tick = 330; frame.RuntimeSeconds = 11;
            recorder.RecordMemoryForTesting(frame, 12, 23, 34, 45);
            frame.StartedTimestamp += 5 * System.Diagnostics.Stopwatch.Frequency; frame.Frame = 3; frame.Focused = false; frame.Paused = true;
            recorder.RecordMemoryForTesting(frame, 13, 24, 35, 46);
            var writer = new StringWriter(); recorder.WriteMemoryCsv(writer);
            var rows = writer.ToString().Trim().Split('\n');
            Assert.That(rows.Length, Is.EqualTo(4));
            Assert.That(rows[1].Trim(), Is.EqualTo("1,100,0,300,10,411,1,0,11,22,33,44"));
            Assert.That(rows[2], Does.Contain(",1,330,11,411,1,0,12,23,34,45"));
            Assert.That(rows[3], Does.Contain(",6,330,11,411,0,1,13,24,35,46"));
            Assert.That(recorder.DroppedMemorySamples, Is.Zero);
        }
        [Test] public void MemoryLedgerReportsOverflowWithoutOverwritingEarliestEvidence()
        {
            using var recorder = new DevelopmentLongFrameRecorder(8, false);
            var frame = Frame(1);
            for (var i = 0; i < 18003; i++)
            {
                frame.StartedTimestamp = i * System.Diagnostics.Stopwatch.Frequency;
                recorder.RecordMemoryForTesting(frame, i, i, i, i);
            }
            Assert.That(recorder.DroppedMemorySamples, Is.EqualTo(2));
            Assert.That(recorder.Overflowed, Is.True);
            var writer = new StringWriter(); recorder.WriteMemoryCsv(writer);
            Assert.That(writer.ToString().Trim().Split('\n').Length, Is.EqualTo(18002));
        }
        [Test] public void ActualWorldCapacityBaselineIncludesCollectionsThatNeverGrow()
        {
            var gameObject = new UnityEngine.GameObject("capacity-baseline-test");
            try
            {
                var world = gameObject.AddComponent<Game.View.WorldRenderer>();
                using var recorder = new DevelopmentLongFrameRecorder(8, false);
                var collections = new HashSet<string>();
                world.RecordDiagnosticCapacityBaselines((name, capacity, count) =>
                {
                    Assert.That(collections.Add(name), Is.True);
                    Assert.That(capacity, Is.GreaterThanOrEqualTo(count));
                    recorder.RecordCapacityBaseline(1, 2, 3, name, capacity, count);
                });
                Assert.That(collections.Count, Is.EqualTo(14));
                Assert.That(collections, Does.Contain("waveMotion.previousProjectiles"));
                Assert.That(collections, Does.Contain("fallowHarvests"));
                var writer = new StringWriter(); recorder.WriteCapacityGrowthCsv(writer);
                var rows = writer.ToString().Trim().Split('\n');
                Assert.That(rows.Length, Is.EqualTo(15));
                for (var i = 1; i < rows.Length; i++) Assert.That(rows[i].Trim(), Does.EndWith(",baseline"));
            }
            finally { UnityEngine.Object.DestroyImmediate(gameObject); }
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
