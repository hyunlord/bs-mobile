using System;
using System.IO;
using System.Threading;
using Game.App;
using NUnit.Framework;

public sealed class GoldenCaptureWriterPipelineTests
{
    [Test]
    public void ParallelJpegCompletionCommitsTimestampsInSequenceAndDoesNotFreeEarly()
    {
        var folder = Path.Combine(Path.GetTempPath(), "capture-parallel-" + Guid.NewGuid().ToString("N"));
        using var firstEntered = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var slots = new[] { new GoldenCaptureFrame(16), new GoldenCaptureFrame(16) };
        var writer = new GoldenCaptureWriter(folder, 2, 2, slots, false, pixels =>
        {
            if (pixels[0] == 11) { firstEntered.Set(); if (!releaseFirst.Wait(5000)) throw new TimeoutException("Test did not release first encoder."); }
            else secondEntered.Set();
            return new[] { pixels[0] };
        });
        try
        {
            Publish(slots[0], 0, 0, 11); Publish(slots[1], 1, .023, 22); writer.Notify();
            Assert.That(firstEntered.Wait(5000), Is.True);
            Assert.That(secondEntered.Wait(5000), Is.True, "Second JPEG must encode while the first JPEG is blocked.");
            Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref slots[1].State) == 4, 5000), Is.True);
            Assert.That(writer.Written, Is.Zero, "Out-of-order encode completion cannot reorder the manifest.");
            Assert.That(Volatile.Read(ref slots[0].State), Is.EqualTo(3));
            writer.Complete(); releaseFirst.Set();
            Assert.That(writer.Join(5000), Is.True); Assert.That(writer.Completed, Is.True); Assert.That(writer.Error, Is.Null);
            Assert.That(writer.Written, Is.EqualTo(2));
            Assert.That(Volatile.Read(ref slots[0].State), Is.Zero); Assert.That(Volatile.Read(ref slots[1].State), Is.Zero);
            var csv = File.ReadAllLines(Path.Combine(folder, "frames.csv"));
            Assert.That(csv.Length, Is.EqualTo(3)); Assert.That(csv[1], Does.StartWith("0,")); Assert.That(csv[2], Does.StartWith("1,"));
            var concat = File.ReadAllText(Path.Combine(folder, "frames.ffconcat"));
            Assert.That(concat, Does.Contain("duration 0.023000000"));
            Assert.That(concat.Split(new[] { "file '" }, StringSplitOptions.None).Length - 1, Is.EqualTo(2));
        }
        finally { releaseFirst.Set(); writer.Complete(); writer.Join(5000); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [TestCase(1)] [TestCase(2)] [TestCase(12)]
    public void BoundedSlotsCanBeReusedWithoutLosingFramesAndDrainOnComplete(int slotCount)
    {
        var folder = Path.Combine(Path.GetTempPath(), "capture-reuse-" + Guid.NewGuid().ToString("N"));
        var slots = new GoldenCaptureFrame[slotCount];
        for (var i = 0; i < slots.Length; i++) slots[i] = new GoldenCaptureFrame(16);
        var writer = new GoldenCaptureWriter(folder, 2, 2, slots, false, pixels => new[] { pixels[0] });
        try
        {
            for (var sequence = 0; sequence < 40; sequence++)
            {
                var slot = slots[sequence % slots.Length];
                Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref slot.State) == 0, 5000), Is.True);
                Publish(slot, sequence, sequence / 60d, (byte)sequence); writer.Notify();
            }
            writer.Complete(); Assert.That(writer.Join(5000), Is.True); Assert.That(writer.Error, Is.Null);
            Assert.That(writer.Written, Is.EqualTo(40));
            var rows = File.ReadAllLines(Path.Combine(folder, "frames.csv")); Assert.That(rows.Length, Is.EqualTo(41));
            for (var sequence = 0; sequence < 40; sequence++)
            {
                Assert.That(rows[sequence + 1], Does.StartWith(sequence + ","));
                Assert.That(File.ReadAllBytes(Path.Combine(folder, "frames", "frame-" + sequence.ToString("D6") + ".jpg")), Is.EqualTo(new[] { (byte)sequence }));
            }
        }
        finally { writer.Complete(); writer.Join(5000); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Test]
    public void EncoderFailureJoinsOtherOwnedWorkBeforePublishingCompletion()
    {
        var folder = Path.Combine(Path.GetTempPath(), "capture-error-" + Guid.NewGuid().ToString("N"));
        using var secondEntered = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        var slots = new[] { new GoldenCaptureFrame(16), new GoldenCaptureFrame(16) };
        var writer = new GoldenCaptureWriter(folder, 2, 2, slots, false, pixels =>
        {
            if (pixels[0] == 1)
            {
                if (!secondEntered.Wait(5000)) throw new TimeoutException("Second encoder did not start.");
                throw new IOException("deliberate encoder failure");
            }
            secondEntered.Set();
            if (!releaseSecond.Wait(5000)) throw new TimeoutException("Test did not release second encoder.");
            return new[] { pixels[0] };
        });
        try
        {
            Publish(slots[0], 0, 0, 1); Publish(slots[1], 1, .02, 2); writer.Complete();
            Assert.That(secondEntered.Wait(5000), Is.True);
            Assert.That(SpinWait.SpinUntil(() => writer.Error != null, 5000), Is.True);
            Assert.That(writer.Completed, Is.False, "Failure cannot expose buffers as complete while another encoder owns them.");
            Assert.That(Volatile.Read(ref slots[1].State), Is.EqualTo(3));
            releaseSecond.Set(); Assert.That(writer.Join(5000), Is.True); Assert.That(writer.Completed, Is.True);
            Assert.That(writer.Error, Does.Contain("deliberate encoder failure")); Assert.That(writer.Written, Is.Zero);
            Assert.That(Volatile.Read(ref slots[0].State), Is.EqualTo(3));
        }
        finally { releaseSecond.Set(); writer.Complete(); writer.Join(5000); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Test]
    public void SequenceGapIsReportedRatherThanSilentlyDiscardingLaterReadyFrames()
    {
        var folder = Path.Combine(Path.GetTempPath(), "capture-gap-" + Guid.NewGuid().ToString("N"));
        var slots = new[] { new GoldenCaptureFrame(16), new GoldenCaptureFrame(16) };
        var writer = new GoldenCaptureWriter(folder, 2, 2, slots, false, pixels => new[] { pixels[0] });
        try
        {
            Publish(slots[1], 1, .02, 2); writer.Complete();
            Assert.That(writer.Join(5000), Is.True);
            Assert.That(writer.Error, Does.Contain("sequence gap")); Assert.That(writer.Written, Is.Zero);
            Assert.That(Volatile.Read(ref slots[1].State), Is.EqualTo(2), "Failed ownership must not claim to release an uncommitted frame.");
        }
        finally { writer.Complete(); writer.Join(5000); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    static void Publish(GoldenCaptureFrame slot, int sequence, double wall, byte marker)
    {
        slot.Sequence = sequence; slot.WallSeconds = wall; slot.Tick = sequence / 2; slot.Pixels[0] = marker;
        Volatile.Write(ref slot.State, 2);
    }
}
