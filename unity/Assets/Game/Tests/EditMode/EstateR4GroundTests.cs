using System;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class EstateR4GroundTests
    {
        [Test]
        public void R7UsesOneContinuousUnmodifiedGrassTileInsteadOfPerCellRestorationPatches()
        {
            var config = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            var state = AccountEstate.NewPrototype(CanonicalContent.CreateMetaCatalog(), config, 0, 0);
            var plan = EstatePlanner.Plan(EstateDecorRules.Load(), EstateArt.Load(), config, state.Estate);
            var tiles = plan.Items.Where(i => i.Source == "terrain").ToArray();
            Assert.That(tiles, Is.Not.Empty);
            Assert.That(tiles.All(i => i.Role == "ground-grass-r4" && i.IsometricGround && i.Colour == Color.white && i.Width == 512 && i.Aspect == .5f), Is.True);
            foreach (var row in tiles.GroupBy(i => i.Grid.y))
            {
                var columns = row.OrderBy(i => i.Grid.x).ToArray();
                for (var i = 1; i < columns.Length; i++) Assert.That(columns[i].Grid.x - columns[i - 1].Grid.x, Is.EqualTo(8));
            }
        }

        [Test]
        public void SoilFullCanvasesAvoidEveryFootprintRoadAndClosedCellDeterministically()
        {
            var config = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            var state = AccountEstate.NewPrototype(CanonicalContent.CreateMetaCatalog(), config, 0, 0);
            var art = EstateArt.Load(); var rules = EstateDecorRules.Load();
            var first = EstatePlanner.Plan(rules, art, config, state.Estate);
            var second = EstatePlanner.Plan(rules, art, config, state.Estate);
            var soils = first.Items.Where(i => i.Source == "soil").ToArray();
            Assert.That(soils, Is.Not.Empty);
            Assert.That(second.Items.Where(i => i.Source == "soil").Select(i => (i.Key, i.Role, i.Grid, i.Width)), Is.EqualTo(soils.Select(i => (i.Key, i.Role, i.Grid, i.Width))));
            var footprints = state.Estate.Buildings.SelectMany(b =>
            {
                var kind = config.Buildings.Single(k => k.Kind == b.Kind);
                return Enumerable.Range(0, kind.Width * kind.Height).Select(i => new Vector2Int(b.X + i % kind.Width, b.Y + i / kind.Width));
            }).ToHashSet();
            foreach (var item in soils)
            {
                var sprite = art.Get(item.Role); var aspect = sprite.rect.height / sprite.rect.width;
                foreach (var c in EstatePlanner.GroundCanvasCells(item.Grid, item.Width, aspect))
                {
                    Assert.That(footprints.Contains(c) || first.Road.Contains(c), Is.False, item.Key + ": canvas overlaps occupied cell " + c);
                    Assert.That(config.Zones.Any(z => state.Estate.OpenZones.Contains(z.Id) && c.x >= z.X && c.x < z.X + z.Width && c.y >= z.Y && c.y < z.Y + z.Height), Is.True);
                }
                Assert.That(EstatePlanner.GroundCanvasCells(item.Grid, item.Width, aspect, .3f).Any(first.Road.Contains), Is.False, "road feather margin");
            }
            TestContext.WriteLine("soil variants: " + string.Join(", ", soils.GroupBy(i => i.Role).Select(g => g.Key + "=" + g.Count())));
        }
    }
}
