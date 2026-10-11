using System;
using System.IO;
using Game.App;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.EditMode
{
    public sealed class AutoplayCaptureTests
    {
        [TestCase("Title.Enter")]
        [TestCase("Title.Settings")]
        [TestCase("Estate.Depart")]
        [TestCase("Departure.Start")]
        [TestCase("Summary.Retry")]
        [TestCase("Card.Choose:core:seed_bag")]
        [TestCase("Cards.Reroll")]
        [TestCase("Pause.Continue")]
        [TestCase("Settings.Back")]
        [TestCase("Settings.AimNearest")]
        public void CapturePrefersStableActionEvenWhenButtonHasNoText(string name)
        {
            var root = new GameObject("capture button regression");
            try
            {
                var unlabeled = CaptureButton(root, name);
                var legacy = CaptureButton(root, "legacy", "old label");
                var selected = AutoplayCapture.ResolveButton(new[] { legacy, unlabeled }, "old label", false, name);
                Assert.That(selected, Is.SameAs(unlabeled));
                var invoked = 0; selected.onClick.AddListener(() => invoked++);
                selected.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(null)
                    { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left });
                Assert.That(invoked, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void LegacyLabelsRemainAvailableBesideUnlabeledAndDisabledPortraitButtons()
        {
            var root = new GameObject("legacy capture regression");
            try
            {
                var title = CaptureButton(root, "Title.Enter");
                var unavailable = CaptureButton(root, "Departure.Start"); unavailable.interactable = false;
                var legacy = CaptureButton(root, "legacy", "개척 시작");
                var buttons = new[] { title, unavailable, legacy };
                Assert.That(AutoplayCapture.ResolveButton(buttons, "개척 시작", false, "Estate.Depart", "Departure.Start"), Is.SameAs(legacy));
                Assert.That(AutoplayCapture.ResolveButton(buttons, "개척", true), Is.SameAs(legacy));
                legacy.gameObject.SetActive(false);
                Assert.That(AutoplayCapture.ResolveButton(buttons, "개척 시작", false), Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void CaptureCardSelectDoesNotChooseBanOrLockAndAmbiguousLabelsFailClosed()
        {
            var root = new GameObject("card capture regression");
            try
            {
                var choose = CaptureButton(root, "Card.Choose:core:seed_bag");
                var ban = CaptureButton(root, "Card.Ban:core:seed_bag", "씨앗 자루");
                var pin = CaptureButton(root, "Card.Lock:core:seed_bag", "씨앗 자루");
                var buttons = new[] { ban, pin, choose };
                Assert.That(AutoplayCapture.ResolveButton(buttons, null, false, "Card.Choose:core:seed_bag", "Choose core:seed_bag"), Is.SameAs(choose));
                Assert.Throws<InvalidOperationException>(() => AutoplayCapture.ResolveButton(buttons, "씨앗 자루", false));
                choose.interactable = false;
                Assert.That(AutoplayCapture.ResolveButton(buttons, null, false, "Card.Choose:core:seed_bag"), Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static Button CaptureButton(GameObject parent, string name, string label = null)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Button)); child.transform.SetParent(parent.transform);
            if (label != null)
            {
                var text = new GameObject("Label", typeof(RectTransform), typeof(Text)); text.transform.SetParent(child.transform);
                text.GetComponent<Text>().text = label;
            }
            return child.GetComponent<Button>();
        }

        [TestCase(60,1)]
        [TestCase(120,2)]
        [TestCase(119.88,2)]
        [TestCase(144,0)]
        [TestCase(180,3)]
        [TestCase(240,4)]
        [TestCase(0,0)]
        public void ExplicitRecordingPresentsAtSixtyInsteadOfDiscardingHalfOfOverloadedRendering(double hz,int divisor)
            => Assert.That(GoldenMinuteCapture.CaptureVSync(hz),Is.EqualTo(divisor));

        [TestCase(120, 1, 2)]
        [TestCase(60, 1, 1)]
        [TestCase(120, 2, 1)]
        [TestCase(144, 1, 0)]
        [TestCase(120, 0, 0)]
        public void CaptureStrideUsesOnlyKnownSynchronizedPresentationRates(double hz, int vSync, int expected)
            => Assert.That(GoldenMinuteCapture.RefreshStride(hz, vSync), Is.EqualTo(expected));

        [Test]
        public void CoreWarmupDoesNotAdvanceAnExistingFreshSession()
        {
            var catalog = Game.App.Generated.CanonicalContent.CreateCatalog();
            var options = new InteractiveOptions(new RunOptions(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.NearestEnemy, new string('a', 64));
            var actual = new InteractiveSession(catalog, options);
            var originalHash = actual.ComputeStateHash();
            Assert.That(AutoplayCapture.PrewarmIsolatedCore(catalog, options), Is.EqualTo(6));
            Assert.That(actual.View.CaptureFrame().Tick, Is.Zero);
            Assert.That(actual.NextSequence, Is.Zero);
            Assert.That(actual.ComputeStateHash(), Is.EqualTo(originalHash));
        }

        [Test]
        public void SmoothnessExternalCommandsDoNotEnterUpdateRows()
        {
            var folder = Path.Combine(Path.GetTempPath(), "external-scope-" + Guid.NewGuid().ToString("N"));
            try
            {
                var diagnostics = new FrameSmoothnessDiagnostics();
                diagnostics.Begin();
                using (diagnostics.Scope(SmoothnessScope.CoreApply)) GC.KeepAlive(new byte[4096]);
                diagnostics.BeginFrame(1, false); diagnostics.EndFrame();
                using (diagnostics.Scope(SmoothnessScope.Recording)) GC.KeepAlive(new byte[4096]);
                diagnostics.BeginFrame(2, false); diagnostics.EndFrame(); diagnostics.Write(folder);
                var lines = File.ReadAllLines(Path.Combine(folder, "smoothness-frames.csv"));
                var columns = lines[0].Split(',');
                for (var i = 1; i < lines.Length; i++)
                {
                    var values = lines[i].Split(',');
                    foreach (var name in new[] { "coreApplyMs", "recordingMs" })
                        Assert.That(values[Array.IndexOf(columns, name)], Is.EqualTo("0.000000"));
                    Assert.That(double.Parse(values[Array.IndexOf(columns, "unattributedUpdateMs")], System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThanOrEqualTo(0));
                    foreach (var name in new[] { "coreApplyBytes", "recordingBytes" })
                        Assert.That(long.Parse(values[Array.IndexOf(columns, name)]), Is.EqualTo(diagnostics.AllocationCounterAvailable ? 0 : -1));
                }
                var external = File.ReadAllLines(Path.Combine(folder, "smoothness-external-scopes.csv"));
                foreach (var name in new[] { "CoreApply", "Recording" })
                {
                    var row = Array.Find(external, line => line.StartsWith(name + ",", StringComparison.Ordinal)).Split(',');
                    Assert.That(row[1], Is.EqualTo("1"));
                    Assert.That(double.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThan(0));
                    if (diagnostics.AllocationCounterAvailable) Assert.That(long.Parse(row[3]), Is.GreaterThanOrEqualTo(4096));
                    else Assert.That(long.Parse(row[3]), Is.EqualTo(-1));
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test]
        public void SmoothnessRestartDoesNotAcceptAnEarlierScope()
        {
            var folder = Path.Combine(Path.GetTempPath(), "restart-scope-" + Guid.NewGuid().ToString("N"));
            try
            {
                var diagnostics = new FrameSmoothnessDiagnostics(); diagnostics.Begin();
                var stale = diagnostics.Scope(SmoothnessScope.CoreApply);
                diagnostics.Begin(); stale.Dispose();
                diagnostics.BeginFrame(1, false); diagnostics.EndFrame(); diagnostics.Write(folder);
                var external = File.ReadAllLines(Path.Combine(folder, "smoothness-external-scopes.csv"));
                var row = Array.Find(external, line => line.StartsWith("CoreApply,", StringComparison.Ordinal)).Split(',');
                Assert.That(row[1], Is.EqualTo("0"));
                Assert.That(row[2], Is.EqualTo("0.000000"));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test]
        public void SmoothnessScopeTimesRemainSeparateFromAllocationCapability()
        {
            var folder = Path.Combine(Path.GetTempPath(), "scope-timing-" + Guid.NewGuid().ToString("N"));
            try
            {
                var diagnostics = new FrameSmoothnessDiagnostics();
                diagnostics.Begin(); diagnostics.BeginFrame(1, false);
                using (diagnostics.Scope(SmoothnessScope.CoreApply)) System.Threading.Thread.SpinWait(10000);
                using (diagnostics.Scope(SmoothnessScope.Recording)) System.Threading.Thread.SpinWait(10000);
                diagnostics.EndFrame(); diagnostics.Write(folder);
                var lines = File.ReadAllLines(Path.Combine(folder, "smoothness-frames.csv"));
                var columns = lines[0].Split(','); var values = lines[1].Split(',');
                Assert.That(values.Length, Is.EqualTo(columns.Length));
                foreach (var name in new[] { "coreApplyMs", "recordingMs", "unattributedUpdateMs" })
                    Assert.That(double.Parse(values[Array.IndexOf(columns, name)], System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThan(0));
                Assert.That(values[Array.IndexOf(columns, "worldMs")], Is.EqualTo("0.000000"));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

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
        public void ScreenPngKeepsRenderedRgbAndDiscardsFramebufferTransparency()
        {
            var pixels = new byte[] { 12, 34, 56, 0, 78, 90, 123, 127 };
            GoldenCaptureWriter.MakeScreenOpaque(pixels);
            Assert.That(pixels, Is.EqualTo(new byte[] { 12, 34, 56, 255, 78, 90, 123, 255 }));
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
