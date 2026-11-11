using System.Collections.Generic;
using Game.App;
using Game.App.Generated;
using Game.Input;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    // #343: autoplay movement decided per Core tick from the state just before it gives the same run however many ticks a
    // rendered frame advances; the former once-per-frame decision did not.
    public sealed class AutoplayTickInputTests
    {
        const int Ticks = 1500;

        // Plays the ordinary autoplay route; framesOf ticks are advanced per "rendered frame". perTick decides the movement before
        // every tick (the new path); otherwise once at the frame's start and reused for its ticks (the former stick sample).
        static (string hash, List<ReplayCommand> commands) Play(int framesOf, bool perTick)
        {
            var catalog = CanonicalContent.CreateCatalog();
            WaveCaptureInput.ConfigureMovement(null); WaveCaptureInput.ConfigurePriority(null);
            var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true),
                AimMode.Movement, CanonicalContent.DataHash, MeleeAim: CatalogRunOptions.MeleeAim(catalog)));
            var commands = new List<ReplayCommand>();
            void Apply(ReplayCommand c) { session.Apply(c); commands.Add(c); }
            PlayerInput Decide() => DesktopControls.ResolveMovement(default, false,
                AutoplayCapture.OrdinaryDirection(session.View.CaptureFrame(), ((IWaveRunView)session.View).CaptureWaveRuntime(), catalog, false, false), false);
            var ticks = 0;
            while (ticks < Ticks && session.View.Status != RunStatus.Completed)
            {
                var frame = session.View.CaptureFrame();
                if (session.View.Status == RunStatus.AwaitingCard)
                {
                    var card = WaveCaptureInput.ChooseCard(session.View.CaptureCards(), frame, catalog);
                    Apply(new ReplayCommand(session.NextSequence, frame.Tick, ReplayCommandKind.ChooseCard, CardId: card));
                    continue;
                }
                var sampled = Decide();
                for (var i = 0; i < framesOf && ticks < Ticks && session.View.Status == RunStatus.Running; i++, ticks++)
                    Apply(new ReplayCommand(session.NextSequence, session.View.CaptureFrame().Tick, ReplayCommandKind.Advance, perTick ? Decide() : sampled));
            }
            return (session.ComputeStateHash(), commands);
        }

        [Test]
        public void TickDecisionsGiveTheSameRunForAnyTicksPerFrame()
        {
            if (!CanonicalContent.IsWave) Assert.Ignore("Requires a wave export.");
            var one = Play(1, true); var two = Play(2, true); var three = Play(3, true);
            Assert.That(two.hash, Is.EqualTo(one.hash));
            Assert.That(three.hash, Is.EqualTo(one.hash));
            Assert.That(two.commands.Count, Is.EqualTo(one.commands.Count));
            for (var i = 0; i < one.commands.Count; i++) Assert.That(two.commands[i], Is.EqualTo(one.commands[i]), "command " + i);
        }

        [Test]
        public void FrameDecisionsDependOnTicksPerFrame()
        {
            if (!CanonicalContent.IsWave) Assert.Ignore("Requires a wave export.");
            Assert.That(Play(3, false).hash, Is.Not.EqualTo(Play(1, false).hash), "the former stick sample reuses one decision for a frame's ticks");
        }

        [Test]
        public void CaptureResultsNameTheTickClock() => Assert.That(AutoplayCapture.InputClock, Is.EqualTo("tick"));
    }
}
