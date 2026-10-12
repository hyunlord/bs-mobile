using System;
using System.Collections.Generic;
using Game.App.Generated;
using SowSiege.Core;
using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class DisplayCadenceTests
    {
        [TestCase(.4792222)] [TestCase(.65)]
        public void OneTickBudgetPreservesDebtUntilLaterFrames(double elapsed)
        {
            const double step = 1d / 30;
            var debt = elapsed; var total = 0;
            for (var frame = 0; frame < 30; frame++)
            {
                var budget = new FrameAdvanceBudget(true); var advances = 0;
                while (budget.TryAdvance(ref debt, step)) { advances++; total++; }
                Assert.That(advances, Is.LessThanOrEqualTo(1));
                Assert.That(debt + total * step, Is.EqualTo(elapsed).Within(1e-12));
            }
            Assert.That(total, Is.EqualTo((int)(elapsed / step)));
            Assert.That(debt, Is.LessThan(step));
        }
        [Test] public void DeferredBudgetKeepsCardCommandsInOrderWithoutChargingThemTicks()
        {
            var commands = new[] { true, false, false, true, false, true };
            var accepted = new System.Collections.Generic.List<int>();
            double debt = .5; const double step = 1d / 30; var next = 0;
            for (var frame = 0; frame < 3; frame++)
            {
                var budget = new FrameAdvanceBudget(true);
                while (debt >= step && next < commands.Length)
                {
                    if (commands[next] && !budget.TryAdvance(ref debt, step)) break;
                    accepted.Add(next++);
                }
            }
            Assert.That(accepted, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
            Assert.That(debt, Is.EqualTo(.4).Within(1e-12));
        }
        [Test] public void DeferredAndLegacyPumpsPreserveActualCardChoiceInDebugFixture()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var options = new InteractiveOptions(new RunOptions(30001, catalog.Tuning.DefaultHero,
                catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash);
            var source = new InteractiveSession(catalog, options);
            var commands = new List<ReplayCommand>();
            void Record(ReplayCommandKind kind, string card = null)
            {
                var command = new ReplayCommand(source.NextSequence, source.View.CaptureFrame().Tick, kind,
                    kind == ReplayCommandKind.Advance ? new PlayerInput(1000, 0) : default, CardId: card);
                source.Apply(command); commands.Add(command);
            }
            for (var tick = 0; tick < 10; tick++) Record(ReplayCommandKind.Advance);
            Record(ReplayCommandKind.GrantLevel);
            Assert.That(source.View.Status, Is.EqualTo(RunStatus.AwaitingCard));
            var offered = source.View.CaptureCards().Cards;
            Assert.That(offered.Count, Is.GreaterThan(0));
            Record(ReplayCommandKind.ChooseCard, offered[0]);
            Assert.That(source.View.Status, Is.EqualTo(RunStatus.Running));
            for (var tick = 0; tick < 30; tick++) Record(ReplayCommandKind.Advance);
            var legacy = new InteractiveSession(catalog, options);
            var deferred = new InteractiveSession(catalog, options);
            var end = commands.Count;
            const double step = 1d / 30;
            foreach (var limited in new[] { false, true })
            {
                var session = limited ? deferred : legacy;
                var next = 0; double debt = 0;
                for (var frame = 0; next < end && frame < end * 3; frame++)
                {
                    debt += frame == 12 ? .65 : 1d / 60;
                    var budget = new FrameAdvanceBudget(limited); var frameAdvances = 0;
                    while (debt >= step && next < end)
                    {
                        var command = commands[next];
                        if (command.Kind == ReplayCommandKind.Advance)
                        {
                            if (!budget.TryAdvance(ref debt, step)) break;
                            frameAdvances++;
                        }
                        session.Apply(command); next++;
                    }
                    if (limited) Assert.That(frameAdvances, Is.LessThanOrEqualTo(1));
                }
                Assert.That(next, Is.EqualTo(end), "No command may be dropped to clear time debt.");
            }
            Assert.That(deferred.ComputeStateHash(), Is.EqualTo(legacy.ComputeStateHash()));
            Assert.That(deferred.ComputeStateHash(), Is.EqualTo(source.ComputeStateHash()));
        }
        [Test] public void OfflineAndFastDebugRetainUnlimitedBudgetAndFixedDeltaCadence()
        {
            double debt = .5; const double step = 1d / 30;
            var fast = new FrameAdvanceBudget(false); var ticks = 0;
            while (fast.TryAdvance(ref debt, step)) ticks++;
            Assert.That(ticks, Is.EqualTo(15));
            debt = 0; ticks = 0;
            for (var frame = 0; frame < 60; frame++)
            {
                debt += 1d / 60; var offline = new FrameAdvanceBudget(false);
                while (offline.TryAdvance(ref debt, step)) ticks++;
            }
            Assert.That(ticks, Is.EqualTo(30));
            Assert.That(debt, Is.EqualTo(0).Within(1e-12));
        }
        [Test] public void TargetSixtyExperimentIsExplicitAndCrowdOnly()
        {
            Assert.That(DisplayCadence.ParseCrowdTarget60Experiment(Array.Empty<string>()), Is.False);
            Assert.That(DisplayCadence.ParseCrowdTarget60Experiment(new[] { "--wave-benchmark", "fixture.ssreplay", "--crowd-replay-benchmark", "--crowd-target60-experiment" }), Is.True);
            Assert.Throws<ArgumentException>(() => DisplayCadence.ParseCrowdTarget60Experiment(new[] { "--crowd-target60-experiment" }));
            Assert.Throws<ArgumentException>(() => DisplayCadence.ParseCrowdTarget60Experiment(new[] { "--wave-benchmark", "fixture.ssreplay", "--crowd-target60-experiment" }));
            Assert.Throws<ArgumentException>(() => DisplayCadence.ParseCrowdTarget60Experiment(new[] { "--wave-benchmark", "--crowd-replay-benchmark", "--crowd-target60-experiment" }));
        }
        [TestCase("--offline-replay-capture")] [TestCase("--autoplay-capture")]
        [TestCase("--wave-benchmark-capture")] [TestCase("--crowd-target60-experiment")] [TestCase("--wave-benchmark-mode")]
        public void TargetSixtyExperimentRejectsCaptureDuplicatesAndAlternateModes(string conflict)
        {
            Assert.Throws<ArgumentException>(() => DisplayCadence.ParseCrowdTarget60Experiment(new[] { "--wave-benchmark", "fixture.ssreplay", "--crowd-replay-benchmark", "--crowd-target60-experiment", conflict }));
        }

        [TestCase(60d, 1)] [TestCase(120d, 2)] [TestCase(59.94d, 1)] [TestCase(119.88d, 2)]
        [TestCase(180d, 3)] [TestCase(240d, 4)] [TestCase(144d, 0)] [TestCase(75d, 0)]
        [TestCase(0d, 0)] [TestCase(double.NaN, 0)] [TestCase(double.PositiveInfinity, 0)]
        public void DesktopUsesSixtyRatherThanNativeRefresh(double refresh, int expected)
            => Assert.That(DisplayCadence.SyncInterval(refresh, false), Is.EqualTo(expected));

        [TestCase(60d)] [TestCase(120d)]
        public void MobileUsesTargetFrameRate(double refresh)
            => Assert.That(DisplayCadence.SyncInterval(refresh, true), Is.Zero);

        [Test] public void MonitorChangeRecomputesInterval()
        {
            Assert.That(DisplayCadence.SyncInterval(120, false), Is.EqualTo(2));
            Assert.That(DisplayCadence.SyncInterval(60, false), Is.EqualTo(1));
            Assert.That(DisplayCadence.SyncInterval(120, false), Is.EqualTo(2));
        }
    }
}
