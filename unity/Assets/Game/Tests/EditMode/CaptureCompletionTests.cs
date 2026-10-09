using System;
using System.Collections.Generic;
using System.IO;
using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class CaptureCompletionTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EvidenceOrCleanupFailureStillAttemptsEveryStepAndExitsNonzero(int failingStep)
        {
            var steps = new List<string>();
            var statuses = new List<bool>();
            var exit = -1;
            CaptureCompletion.Run(true,
                () => { steps.Add("ledger"); if (failingStep == 0) throw new IOException("ledger failed"); },
                () => { steps.Add("cleanup"); if (failingStep == 1) throw new IOException("dispose failed"); },
                (success, error) => { steps.Add("result"); statuses.Add(success); if (failingStep == 2) throw new IOException("result failed"); },
                code => { steps.Add("exit"); exit = code; });
            Assert.That(steps[0], Is.EqualTo("ledger"));
            Assert.That(steps[1], Is.EqualTo("cleanup"));
            Assert.That(steps, Does.Contain("result"));
            Assert.That(steps[steps.Count - 1], Is.EqualTo("exit"));
            Assert.That(statuses[statuses.Count - 1], Is.False);
            Assert.That(exit, Is.EqualTo(1));
        }

        [Test]
        public void AllIoFailuresStillReachExactlyOneFailureExit()
        {
            var exits = new List<int>();
            Assert.DoesNotThrow(() => CaptureCompletion.Run(true,
                () => throw new IOException("ledger"), () => throw new IOException("cleanup"),
                (_, __) => throw new IOException("result"), exits.Add));
            Assert.That(exits, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void ResultWriteFailureRetriesAsFailureWithoutMaskingOriginalError()
        {
            var writes = 0;
            var exit = -1;
            bool? accepted = null;
            Exception recordedError = null;
            CaptureCompletion.Run(true, () => { }, () => { }, (success, error) =>
            {
                writes++;
                if (writes == 1) throw new IOException("disk unavailable");
                accepted = success;
                recordedError = error;
            }, code => exit = code);
            Assert.That(writes, Is.EqualTo(2));
            Assert.That(accepted, Is.False);
            Assert.That(recordedError.ToString(), Does.Contain("disk unavailable"));
            Assert.That(exit, Is.EqualTo(1));
        }

        [TestCase(true, 0)]
        [TestCase(false, 1)]
        public void CleanFinalizationPreservesOriginalOutcome(bool requestedSuccess, int expectedExit)
        {
            var exit = -1;
            bool? accepted = null;
            Exception recordedError = null;
            CaptureCompletion.Run(requestedSuccess, () => { }, () => { }, (success, error) =>
            {
                accepted = success;
                recordedError = error;
            }, code => exit = code);
            Assert.That(exit, Is.EqualTo(expectedExit));
            Assert.That(accepted, Is.EqualTo(requestedSuccess));
            Assert.That(recordedError, Is.Null);
        }
    }

    public sealed class CaptureRestartGateTests
    {
        [Test]
        public void SlowSceneReplacementWaitsThenAcceptsOnlyNewAdvancedRun()
        {
            Assert.That(CaptureRestartGate.IsReady(3, "old", "old", false, 27000), Is.False);
            Assert.That(CaptureRestartGate.IsReady(8, "old", null, false, 0), Is.False);
            Assert.That(CaptureRestartGate.IsReady(12, "old", "new", true, 0), Is.False);
            Assert.That(CaptureRestartGate.IsReady(13, "old", "new", true, 1), Is.True);
        }

        [Test]
        public void OldSessionOrNonrunningReplacementCannotPass()
        {
            Assert.That(CaptureRestartGate.IsReady(10, "old", "old", true, 100), Is.False);
            Assert.That(CaptureRestartGate.IsReady(10, "old", "new", false, 100), Is.False);
        }

        [Test]
        public void MissingOrLateReplacementFailsAtRealDeadline()
        {
            Assert.That(CaptureRestartGate.IsReady(29.99, "old", "old", false, 27000), Is.False);
            Assert.Throws<TimeoutException>(() => CaptureRestartGate.IsReady(30, "old", "old", false, 27000));
            Assert.Throws<TimeoutException>(() => CaptureRestartGate.IsReady(31, "old", "new", true, 1));
        }
    }
}
