using System;
using Game.App;
using NUnit.Framework;
using SowSiege.Core;

public sealed class FeelTrialTests
{
    [Test] public void OrdinaryLaunchDoesNotEnableAnyTrial()
        => Assert.That(FeelTrialOptions.Parse(Array.Empty<string>()), Is.Null);

    [TestCase("speed", false, false, false)]
    [TestCase("speed-feedback", false, false, true)]
    [TestCase("speed-contact", true, false, false)]
    [TestCase("all", true, true, true)]
    public void CombinationsChangeOnlyRequestedSwitches(string mode, bool contact, bool health, bool feedback)
    {
        var value = FeelTrialOptions.Parse(new[] { "--feel-trial", mode });
        Assert.That(value.Rules.FasterLord, Is.True);
        Assert.That(value.Rules.ImmediateContact, Is.EqualTo(contact));
        Assert.That(value.Rules.ReadableHealth, Is.EqualTo(health));
        Assert.That(value.Feedback, Is.EqualTo(feedback));
        Assert.That(FeelTrialOptions.Seed, Is.EqualTo(30001));
        Assert.That(FeelTrialOptions.DurationSeconds, Is.EqualTo(180));
    }

    [TestCase("--autoplay-capture")]
    [TestCase("--normal-gc-probe")]
    [TestCase("--wave-benchmark")]
    [TestCase("--capture-seed")]
    public void TrialsRejectCompetingAutomation(string flag)
        => Assert.Throws<ArgumentException>(() => FeelTrialOptions.Parse(new[] { "--feel-trial", "speed", flag }));

    [Test] public void TrialsRejectUnknownAndRepeatedModes()
    {
        Assert.Throws<ArgumentException>(() => FeelTrialOptions.Parse(new[] { "--feel-trial", "other" }));
        Assert.Throws<ArgumentException>(() => FeelTrialOptions.Parse(new[] { "--feel-trial", "speed", "--feel-trial", "all" }));
    }

    [Test] public void HitTotalsCountActualDamageOnceRatherThanHealthDeltaAfterHealing()
    {
        var damage = new FeelTrialDamage();
        var hit = new WaveEvent(1, 3, "lord-hit", "raider", 0, default, default, 7);
        damage.Observe(hit); damage.Observe(hit);
        damage.Observe(new WaveEvent(2, 4, "heal", "repair", 0, default, default, 5));
        damage.Observe(new WaveEvent(3, 5, "lord-hit", "raider", 0, default, default, 2));
        damage.Observe(new WaveEvent(4, 6, "lord-hit", "raider", 0, default, default, 0));
        Assert.That(damage.Hits, Is.EqualTo(2));
        Assert.That(damage.LostHealth, Is.EqualTo(9));
    }
}
