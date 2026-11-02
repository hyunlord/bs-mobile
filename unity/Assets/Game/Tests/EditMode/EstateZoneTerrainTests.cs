using System.Linq;
using System.Collections.Generic;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class EstateZoneTerrainTests
    {
        [TestCase("", 2166136261u)]
        [TestCase("a", 3826002220u)]
        public void CandidateHashUsesStableUnsignedFnv(string value, uint expected)
        {
            Assert.That(EstateZoneTerrain.Hash(value), Is.EqualTo(expected));
        }

        [Test] public void ApprovedRuntimeVariantsShareOneTextureAndHaveSourceAnchors()
        {
            var art = EstateArt.Load();
            var ids = art.SpriteIds.Where(id => id.StartsWith("zone-")).ToArray();
            Assert.That(ids.Length, Is.EqualTo(128));
            Assert.That(ids.Select(id => art.Get(id).texture).Distinct().Count(), Is.EqualTo(1));
            for (var shape = 1; shape <= 6; shape++)
                foreach (var size in new[] { "S", "M", "L" })
                    foreach (var state in new[] { "bright", "dark" })
                        Assert.That(art.Has($"zone-field-between-0{shape}-{size}-{state}"), Is.True);
            var pivot = art.Pivot("zone-forest-cluster-01-bright");
            Assert.That(pivot.x, Is.EqualTo(.5f).Within(1e-6f));
            Assert.That(pivot.y, Is.EqualTo(1 - 140f / 176f).Within(1e-6f));
        }

        [Test] public void PlanningIsRepeatableAndHistoricalSettingsDoNotReceiveNewLayers()
        {
            var art = EstateArt.Load(); var rules = EstateDecorRules.Load();
            var catalog = CanonicalContent.CreateMetaCatalog();
            foreach (var version in new[] { "m3a-prototype-r7", "m3a-prototype-r8" })
            {
                var config = CanonicalContent.CreateAccountEstateConfig(version);
                var state = AccountEstate.NewPrototype(catalog, config, 0, 0);
                var first = EstatePlanner.Plan(rules, art, config, state.Estate).Items.Where(i => i.Source.StartsWith("zone-")).ToArray();
                var second = EstatePlanner.Plan(rules, art, config, state.Estate).Items.Where(i => i.Source.StartsWith("zone-")).ToArray();
                Assert.That(second.Select(i => (i.Key, i.Role, i.Grid, i.Width)), Is.EqualTo(first.Select(i => (i.Key, i.Role, i.Grid, i.Width))));
                if (version == "m3a-prototype-r7") Assert.That(first, Is.Empty);
                else
                {
                    Assert.That(first, Is.Empty, "Approved static layout replaces automatic candidates.");
                    var layout = EstateAuthoredLayout.Load(config);
                    Assert.That(layout.instances.Length, Is.EqualTo(2055));
                    Assert.That(layout.instances.Any(i => i.category == "cluster"), Is.True);
                    Assert.That(layout.instances.Any(i => i.zoneId == "exterior"), Is.True);
                }
            }
        }

        [Test] public void BlockedCandidatesKeepTheirReservationInsteadOfRefillingNeighbours()
        {
            var candidates = new List<Rect>();
            var first = new Rect(0, 0, 10, 10);
            Assert.That(EstateZoneTerrain.ReserveCandidate(first, candidates, new[] { first }, true), Is.False);
            Assert.That(candidates, Is.EqualTo(new[] { first }));
            Assert.That(EstateZoneTerrain.ReserveCandidate(new Rect(5, 0, 10, 10), candidates, new Rect[0], true), Is.False);
            Assert.That(EstateZoneTerrain.ReserveCandidate(new Rect(20, 0, 10, 10), candidates, new Rect[0], true), Is.True);
        }

        [TestCase("zone:start")] [TestCase("zone:inner-village")] [TestCase("zone:inner-windmill")]
        [TestCase("zone:outskirts-north")] [TestCase("zone:outskirts-east")]
        [TestCase("zone:outskirts-south")] [TestCase("zone:outskirts-west")]
        public void PreservedLowDecorationThemesDoNotUseForestReplacement(string id)
        {
            Assert.That(EstateZoneTerrain.PreserveExisting(id), Is.True);
            Assert.That(EstateZoneTerrain.PreserveExisting("zone:inner-northeast-wood"), Is.False);
        }

        [Test] public void ProjectedReservationsIncludeTallArtAboveItsFootprint()
        {
            var bounds = EstateZoneTerrain.ProjectedBounds(Vector2.zero, 100, .5f, new Vector2(.5f, .1f));
            Assert.That(bounds, Is.EqualTo(new Rect(-50, -180, 100, 200)));
            Assert.That(bounds.Overlaps(new Rect(-10, -120, 20, 10)), Is.True);
        }
    }
}
