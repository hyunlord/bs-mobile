using System.Collections.Generic;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;

namespace Tests.EditMode
{
    public sealed class CatalogRunOptionsTests
    {
        [Test]
        public void LoadedGearDeclarationAloneEnablesV6WhileHistoricalCatalogRemainsDisabled()
        {
            var catalog = CanonicalContent.CreateCatalog();
            if (catalog.WaveRuntime == null) Assert.Ignore("Requires a wave catalog.");
            var gear = new Dictionary<string, WaveGearDefinition>();
            foreach (var entry in catalog.WaveRuntime.Gear) gear.Add(entry.Key, entry.Value with { Aim = null });
            var baseline = catalog with { WaveRuntime = catalog.WaveRuntime with { Gear = gear } };
            Assert.That(CatalogRunOptions.MeleeAim(baseline), Is.Null);
            Assert.That(CatalogRunOptions.MeleeAim(catalog with { WaveRuntime = null }), Is.Null);
            foreach (var entry in catalog.WaveRuntime.Gear)
                if (entry.Value.Kind == WaveAttackKind.Arc) { gear[entry.Key] = entry.Value with { Aim = "nearest-enemy" }; break; }
            var options = new InteractiveOptions(new RunOptions(30001, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true),
                AimMode.Movement, CanonicalContent.DataHash, MeleeAim: CatalogRunOptions.MeleeAim(baseline));
            Assert.That(options.MeleeAim, Is.Not.Null);
            Assert.That(ReplayCodec.Header(options).FormatVersion, Is.EqualTo(6));
            var session = new InteractiveSession(baseline, options);
            session.Apply(new ReplayCommand(0, 0, ReplayCommandKind.Advance));
            Assert.That(session.View.CaptureFrame().Tick, Is.EqualTo(1));
        }
        [TestCase("wave-1a", true)]
        [TestCase("m1-showcase-review", true)]
        [TestCase("first-playable", false)]
        public void ReviewSharesWaveArtWithoutRenamingProfile(string profile, bool expected)
        {
            var before = ArtCatalog.ProfileName;
            try { ArtCatalog.ProfileName = profile; Assert.That(ArtCatalog.UsesWaveArt, Is.EqualTo(expected)); Assert.That(ArtCatalog.ProfileName, Is.EqualTo(profile)); }
            finally { ArtCatalog.ProfileName = before; }
        }
    }
}
