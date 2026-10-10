using System;
using System.IO;
using Game.App;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class AutoplayCaptureTests
    {
        [Test]
        public void GoldenRecordingCannotArmBeforeWorldAndBuffersAreReady()
        {
            var host = new GameObject("capture arming contract");
            try
            {
                var capture = host.AddComponent<GoldenMinuteCapture>();
                Assert.That(capture.ReadyToStart, Is.False);
                Assert.Throws<InvalidOperationException>(() => capture.ArmRecording());
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void OrdinaryCaptureIgnoresGoldenWorldReadyNotification()
        {
            var host = new GameObject("ordinary capture notification");
            try
            {
                var capture = host.AddComponent<AutoplayCapture>();
                Assert.DoesNotThrow(() => capture.OnRunReady(null));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void OrdinaryLaunchAndOutputFlagAloneNeverActivateAutomation()
        {
            Assert.That(AutoplayCapture.IsRequested(new[] { "game" }), Is.False);
            Assert.That(AutoplayCapture.IsRequested(new[] { "game", "--capture-output", "/tmp/example" }), Is.False);
            Assert.That(AutoplayCapture.IsRequested(new[] { "game", "--autoplay-capture=false" }), Is.False);
            Assert.That(AutoplayCapture.IsRequested(new[] { "game", "--autoplay-capture" }), Is.True);
        }

        [Test]
        public void GoldenMinuteRequiresExplicitAutoplayOptIn()
        {
            Assert.That(AutoplayCapture.IsGoldenMinuteRequested(new[] { "game", "--golden-minute" }), Is.False);
            Assert.That(AutoplayCapture.IsGoldenMinuteRequested(new[] { "game", "--autoplay-capture" }), Is.False);
            Assert.That(AutoplayCapture.IsGoldenMinuteRequested(new[] { "game", "--autoplay-capture", "--golden-minute" }), Is.True);
        }

        [Test]
        public void ExplicitOutputPreservesSpacesAndDefaultOutputIsUnique()
        {
            var root = Path.Combine(Path.GetTempPath(), "native capture");
            Assert.That(AutoplayCapture.ParseOutput(new[] { "game", "--capture-output", root }, "/unused"), Is.EqualTo(root));
            var first = AutoplayCapture.ParseOutput(Array.Empty<string>(), root);
            var second = AutoplayCapture.ParseOutput(Array.Empty<string>(), root);
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(Path.GetDirectoryName(first), Is.EqualTo(root));
        }

        [Test]
        public void MalformedOutputArgumentsFailClosed()
        {
            Assert.Throws<ArgumentException>(() => AutoplayCapture.ParseOutput(new[] { "--capture-output" }, "/unused"));
            Assert.Throws<ArgumentException>(() => AutoplayCapture.ParseOutput(new[] { "--capture-output", "--autoplay-capture" }, "/unused"));
            Assert.Throws<ArgumentException>(() => AutoplayCapture.ParseOutput(new[] { "--capture-output", "/one", "--capture-output", "/two" }, "/unused"));
        }

        [Test]
        public void ExistingEvidenceCannotBeOverwritten()
        {
            var root = Path.Combine(Path.GetTempPath(), "capture-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Assert.DoesNotThrow(() => AutoplayCapture.RequireEmptyOutput(root));
                File.WriteAllText(Path.Combine(root, "evidence.txt"), "preserve");
                Assert.Throws<IOException>(() => AutoplayCapture.RequireEmptyOutput(root));
                Assert.That(File.ReadAllText(Path.Combine(root, "evidence.txt")), Is.EqualTo("preserve"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void MixedAudioCaptureIsolatesListenerFromSourcesAndRestoresIt()
        {
            var root = new GameObject("Audio capture test");
            var path = Path.Combine(Path.GetTempPath(), "mixed-audio-" + Guid.NewGuid().ToString("N") + ".wav");
            GoldenMinuteAudio capture = null;
            try
            {
                var listener = root.AddComponent<AudioListener>();
                root.AddComponent<AudioSource>(); root.AddComponent<AudioSource>();
                capture = GoldenMinuteAudio.BeginMixedOutput(listener, path);
                Assert.That(capture.gameObject, Is.Not.SameAs(root));
                Assert.That(capture.GetComponents<AudioSource>(), Is.Empty);
                Assert.That(capture.GetComponents<AudioListener>().Length, Is.EqualTo(1));
                Assert.That(listener.enabled, Is.False);
                capture.StopAudio();
                Assert.That(capture.Join(5000), Is.True, "Audio writer must drain before reading its header.");
                Assert.That(listener.enabled, Is.True);
                Assert.That(capture.GetComponent<AudioListener>().enabled, Is.False);
                Assert.That(new FileInfo(path).Length, Is.GreaterThanOrEqualTo(44));
            }
            finally
            {
                capture?.StopAudio();
                UnityEngine.Object.DestroyImmediate(root);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void CaptureWriterPreservesRealTimestampsWithoutDuplicatingLastFrame()
        {
            var folder = Path.Combine(Path.GetTempPath(), "capture-writer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var slots = new[] { new GoldenCaptureFrame(16), new GoldenCaptureFrame(16) };
            var writer = new GoldenCaptureWriter(folder, 2, 2, slots);
            try
            {
                slots[0].Sequence = 0; slots[0].WallSeconds = 0; slots[0].RenderFrame = 100; slots[0].InitialRun = true;
                slots[1].Sequence = 1; slots[1].WallSeconds = .041; slots[1].RenderFrame = 103;
                System.Threading.Volatile.Write(ref slots[0].State, 2);
                System.Threading.Volatile.Write(ref slots[1].State, 2);
                writer.Complete();
                Assert.That(writer.Join(5000), Is.True);
                Assert.That(writer.Error, Is.Null);
                Assert.That(writer.Written, Is.EqualTo(2));
                Assert.That(writer.InitialRunWritten, Is.True);
                Assert.That(File.Exists(Path.Combine(folder, "02-native-run.png")), Is.True);
                var concat = File.ReadAllText(Path.Combine(folder, "frames.ffconcat"));
                Assert.That(concat, Does.Contain("duration 0.041000000"));
                Assert.That(concat.Split(new[] { "file '" }, StringSplitOptions.None).Length - 1, Is.EqualTo(2));
                Assert.That(File.ReadAllText(Path.Combine(folder, "frames.csv")), Does.Contain("103"));
            }
            finally { writer.Complete(); writer.Join(5000); Directory.Delete(folder, true); }
        }

        [TestCase(.02, 3000, 600)]
        [TestCase(.024, 2500, 1100)]
        public void CumulativeCadenceRejectsSustainedSlowFramesDespiteSmallIndividualGaps(double interval, int intervals, int missing)
        {
            Assert.That(GoldenMinuteCapture.CumulativeMissingSlots(interval * intervals, intervals + 1), Is.EqualTo(missing));
        }

        [Test]
        public void CumulativeCadenceHasOneSharedClockGraceAndPreservesDroppedFrameDeficit()
        {
            Assert.That(GoldenMinuteCapture.CumulativeMissingSlots(60, 3601), Is.Zero);
            Assert.That(GoldenMinuteCapture.CumulativeMissingSlots(60.002, 3601), Is.Zero);
            var boundary = 60 + .002 + .5 / 60;
            Assert.That(GoldenMinuteCapture.CumulativeMissingSlots(boundary - .000001, 3601), Is.Zero);
            Assert.That(GoldenMinuteCapture.CumulativeMissingSlots(boundary + .000001, 3601), Is.EqualTo(1));
            Assert.That(GoldenMinuteCapture.CumulativeMissingSlots(2d / 60, 2), Is.EqualTo(1));
        }

        [Test]
        public void ReadbackRowCorrectionPreservesPixelsAndUsesProvidedScratch()
        {
            var pixels = new byte[] { 1, 2, 3, 4, 5, 6 };
            var scratch = new byte[2];
            GoldenCaptureWriter.FlipRows(pixels, 2, 3, scratch);
            Assert.That(pixels, Is.EqualTo(new byte[] { 5, 6, 3, 4, 1, 2 }));
            GoldenCaptureWriter.FlipRows(pixels, 2, 3, scratch);
            Assert.That(pixels, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void AllocationProbeReportsUnavailableInsteadOfCertifyingAZeroCounter()
        {
            var available = AllocationCounterProbe.CurrentThreadAvailable();
            Assert.That(AllocationCounterProbe.CurrentThreadAvailable(), Is.EqualTo(available));
            if (!available) return;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var known = new byte[1024];
            var after = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(known);
            Assert.That(after - before, Is.GreaterThanOrEqualTo(1024));
        }

        [Test]
        public void ReplayAuditOnlyAcceptsMovementCardChoiceAndAimSetting()
        {
            foreach (ReplayCommandKind kind in Enum.GetValues(typeof(ReplayCommandKind)))
                Assert.That(AutoplayCapture.IsOrdinaryCommand(kind), Is.EqualTo(kind == ReplayCommandKind.Advance || kind == ReplayCommandKind.ChooseCard || kind == ReplayCommandKind.SetAimMode), kind.ToString());
            Assert.That(AutoplayCapture.IsOrdinaryCommand((ReplayCommandKind)999), Is.False);
        }
    }
}
