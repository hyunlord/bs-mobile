using System;
using System.IO;
using Game.App;
using NUnit.Framework;
using SowSiege.Core;

namespace Tests.EditMode
{
    public sealed class AutoplayCaptureTests
    {
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
        public void ReplayAuditOnlyAcceptsMovementCardChoiceAndAimSetting()
        {
            foreach (ReplayCommandKind kind in Enum.GetValues(typeof(ReplayCommandKind)))
                Assert.That(AutoplayCapture.IsOrdinaryCommand(kind), Is.EqualTo(kind == ReplayCommandKind.Advance || kind == ReplayCommandKind.ChooseCard || kind == ReplayCommandKind.SetAimMode), kind.ToString());
            Assert.That(AutoplayCapture.IsOrdinaryCommand((ReplayCommandKind)999), Is.False);
        }
    }
}
