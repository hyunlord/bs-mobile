using System;
using System.IO;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class RecordingTelemetryTests
    {
        string root;
        [SetUp] public void Setup() => root = Path.Combine(Path.GetTempPath(), "recording-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        [Test] public void PauseSnapshotReplaysThenResumeReplacesTerminalAndFinishClosesStream()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var options = new InteractiveOptions(new RunOptions(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash);
            var session = new InteractiveSession(catalog, options);
            using var recording = new RunRecording(root, options, "test");
            var command = new ReplayCommand(0, 0, ReplayCommandKind.Advance);
            session.Apply(command); recording.WriteAccepted(command); recording.Checkpoint(session);
            using (var first = File.OpenRead(recording.ReplayPath)) Assert.That(ReplayRunner.Verify(catalog, options.DataHash, ReplayCodec.Read(first)).Tick, Is.EqualTo(1));
            command = new ReplayCommand(1, 1, ReplayCommandKind.Advance);
            session.Apply(command); recording.WriteAccepted(command); recording.Finish(session, ReplayEndKind.Quit);
            using (var final = File.OpenRead(recording.ReplayPath)) Assert.That(ReplayRunner.Verify(catalog, options.DataHash, ReplayCodec.Read(final)).Tick, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => recording.WriteAccepted(command));
            recording.Finish(session, ReplayEndKind.Quit); recording.Dispose(); recording.Dispose();
        }
        [Test] public void OmittedCommandsAndWrongTerminalFailClosed()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var options = new InteractiveOptions(new RunOptions(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash);
            var session = new InteractiveSession(catalog, options);
            using var recording = new RunRecording(root, options, "test");
            Assert.Throws<InvalidOperationException>(() => recording.Finish(session, ReplayEndKind.Duration));
            session.Apply(new ReplayCommand(0, 0, ReplayCommandKind.Advance));
            Assert.Throws<InvalidOperationException>(() => recording.Checkpoint(session));
            Assert.That(File.Exists(recording.ReplayPath), Is.False);
        }
        [Test] public void TelemetryRetainsStuttersPausedAndAcceleratedSamples()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(1, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var frame = session.View.CaptureFrame();
            using var telemetry = new FrameTelemetry(root, "session", "build", "data", 100, new DeviceFacts());
            telemetry.Capture(.016f, frame with { Tick = 74 }, 1, 2, new Rect(0,0,400,800), new Rect(0,0,400,800), false);
            telemetry.Capture(.5f, frame with { Tick = 80 }, 1, 3, new Rect(0,0,400,800), new Rect(0,0,400,800), false);
            telemetry.Capture(1f, frame with { Tick = 80 }, 4, 4, new Rect(0,0,400,800), new Rect(0,0,400,800), true);
            telemetry.Capture(.02f, frame with { Tick = 100 }, 1, 5, new Rect(0,0,400,800), new Rect(0,0,400,800), false);
            telemetry.Finish(); telemetry.Dispose();
            Assert.That(File.ReadAllLines(telemetry.CsvPath).Length, Is.EqualTo(5));
            var summary = JsonUtility.FromJson<Summary>(File.ReadAllText(telemetry.SummaryPath));
            Assert.That(summary.lateComplete, Is.True); Assert.That(summary.frameP95Ms, Is.EqualTo(500).Within(.001));
            Assert.That(summary.lateSampleCount, Is.EqualTo(2));
        }
        [Test] public void DelayedIntervalAttributesTransitionAndTerminalStallsToPreviousActiveFrame()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(1, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var frame = session.View.CaptureFrame(); var screen = new Rect(0,0,400,800);
            using var telemetry = new FrameTelemetry(root,"session","build","data",100,new DeviceFacts());
            telemetry.BeginInterval(frame with { Tick = 74 },1,0,screen,screen,false); telemetry.CompleteInterval(.016f);
            telemetry.BeginInterval(frame with { Tick = 80 },1,0,screen,screen,false);
            telemetry.CompleteInterval(.6f); // Measured next update, when the card menu is already open.
            telemetry.BeginInterval(frame with { Tick = 80 },4,0,screen,screen,true); telemetry.CompleteInterval(2f);
            telemetry.BeginInterval(frame with { Tick = 100 },1,0,screen,screen,false); telemetry.CompleteInterval(.8f);
            telemetry.Finish();
            var summary = JsonUtility.FromJson<Summary>(File.ReadAllText(telemetry.SummaryPath));
            Assert.That(summary.lateComplete,Is.True); Assert.That(summary.lateSampleCount,Is.EqualTo(2)); Assert.That(summary.frameP95Ms,Is.EqualTo(800).Within(.001));
            var rows = File.ReadAllLines(telemetry.CsvPath);
            Assert.That(rows[2].Split(',')[3],Is.EqualTo("1")); Assert.That(rows[2].Split(',')[4],Is.EqualTo("0"));
        }
        [Test] public void SuspendedGapAndPartialTerminalAreRetainedButCannotClaimCompleteWindow()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(1,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed", ManualCards: true),AimMode.Movement,CanonicalContent.DataHash));
            var frame=session.View.CaptureFrame();var screen=new Rect(0,0,400,800);
            using var telemetry=new FrameTelemetry(root,"session","build","data",100,new DeviceFacts());
            telemetry.BeginInterval(frame with {Tick=74},1,0,screen,screen,false);telemetry.CompleteInterval(.016f);
            telemetry.BeginInterval(frame with {Tick=80},1,0,screen,screen,false);telemetry.CompleteInterval(20f,suspended:true);
            telemetry.BeginInterval(frame with {Tick=100},1,0,screen,screen,false);telemetry.CompleteInterval(.5f,partial:true);
            telemetry.Finish();var summary=JsonUtility.FromJson<Summary>(File.ReadAllText(telemetry.SummaryPath));
            Assert.That(summary.lateComplete,Is.False);Assert.That(summary.lateSampleCount,Is.Zero);Assert.That(File.ReadAllLines(telemetry.CsvPath).Length,Is.EqualTo(4));
        }
        [Test] public void SteadyCaptureUsesNoMainThreadAllocationsAndFinishDrainsEverySample()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(1, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var frame = session.View.CaptureFrame(); var screen = new Rect(0, 0, 400, 800);
            using var telemetry = new FrameTelemetry(root, "allocation", "build", "data", 100, new DeviceFacts());
            for (var i = 0; i < 32; i++) telemetry.Capture(.016f, frame, 1, 0, screen, screen, false);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 512; i++) telemetry.Capture(.016f, frame, 1, 0, screen, screen, false);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            telemetry.Finish();
            Assert.That(allocated, Is.Zero, "Capture must enqueue values, not format CSV on the game thread.");
            Assert.That(File.ReadAllLines(telemetry.CsvPath).Length, Is.EqualTo(545));
        }
        [Serializable] sealed class Summary
 { public bool lateComplete; public double frameP95Ms; public int lateSampleCount; }
    }
}
