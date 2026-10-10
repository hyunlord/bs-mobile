using System;
using System.IO;
using Game.App;
using NUnit.Framework;

public sealed class CaptureSegmentWindowTests
{
    [Test] public void SuspendedDiagnosticsResumeWithOnlyTheChosenSegment()
    {
        var d = new FrameSmoothnessDiagnostics();
        var folder = Path.Combine(Path.GetTempPath(), "capture-segment-" + Guid.NewGuid().ToString("N"));
        try
        {
            d.Begin(); d.BeginFrame(0, false); d.EndFrame();
            d.Suspend(); Assert.That(d.Enabled, Is.False);
            d.BeginFrame(100, false); d.EndFrame();
            d.Begin(); Assert.That(d.Enabled, Is.True);
            d.BeginFrame(7200, false); d.EndFrame(); d.Write(folder);
            var lines = File.ReadAllLines(Path.Combine(folder, "smoothness-frames.csv"));
            Assert.That(lines.Length, Is.EqualTo(2));
            var tickColumn = Array.IndexOf(lines[0].Split(','), "tick");
            Assert.That(tickColumn, Is.GreaterThanOrEqualTo(0));
            Assert.That(lines[1].Split(',')[tickColumn], Is.EqualTo("7200"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Test] public void DefaultKeepsFirstSixtySeconds()
    {
        var s = CaptureSegmentWindow.Parse(Array.Empty<string>());
        Assert.That(s.StartSeconds, Is.Zero); Assert.That(s.EndSeconds, Is.EqualTo(60));
        Assert.That(s.Ready(0, 30, false), Is.True);
        Assert.That(s.Complete(1799, 30), Is.False); Assert.That(s.Complete(1800, 30), Is.True);
    }
    [Test] public void LateWindowWaitsForRealTickAndEndsAfterThirtyGameplaySeconds()
    {
        var s = CaptureSegmentWindow.Parse(new[] { "--capture-start-seconds", "240", "--capture-duration-seconds", "30" });
        Assert.That(s.Ready(7199, 30, false), Is.False); Assert.That(s.Ready(7200, 30, false), Is.True);
        Assert.That(s.Complete(8099, 30), Is.False); Assert.That(s.Complete(8100, 30), Is.True);
        Assert.Throws<InvalidOperationException>(() => s.Ready(7000, 30, true));
        Assert.Throws<InvalidOperationException>(() => s.Ready(8000, 30, true));
    }
    [TestCase(-1,30)] [TestCase(841,30)] [TestCase(240,0)] [TestCase(240,61)]
    public void RejectsUnboundedWindows(int start, int duration) => Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureSegmentWindow(start,duration));
    [Test] public void LandscapeRequiresExplicitLaterSegment()
    {
        Assert.Throws<ArgumentException>(() => CaptureSegmentWindow.Parse(new[] { "--capture-landscape" }));
        Assert.That(CaptureSegmentWindow.Parse(new[] { "--capture-start-seconds", "240", "--capture-landscape" }).Landscape, Is.True);
        Assert.That(CaptureSegmentWindow.Parse(Array.Empty<string>()).Landscape, Is.False);
    }
    [Test] public void RejectsMissingDuplicateAndNonIntegerArguments()
    {
        Assert.Throws<ArgumentException>(() => CaptureSegmentWindow.Parse(new[] { "--capture-start-seconds" }));
        Assert.Throws<ArgumentException>(() => CaptureSegmentWindow.Parse(new[] { "--capture-start-seconds", "1.5" }));
        Assert.Throws<ArgumentException>(() => CaptureSegmentWindow.Parse(new[] { "--capture-duration-seconds", "30", "--capture-duration-seconds", "40" }));
    }
}
