using System;
using System.Collections.Generic;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    // #372/#375: the estate map's painted plan — Astra art coverage, Core level to art stage, restoration colours and the
    // placement invariants on the game grid.
    public sealed class EstateDecorTests
    {
        static (AccountEstateConfig Config, MetaCatalog Catalog, MetaState State) Estate()
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var config = CanonicalContent.CreateAccountEstateConfig();
            return (config, catalog, AccountEstate.NewPrototype(catalog, config, 0, 0));
        }
        static HashSet<Vector2Int> Footprints(AccountEstateConfig config, MetaEstateState estate) =>
            estate.Buildings.SelectMany(b => { var k = config.Buildings.Single(x => x.Kind == b.Kind); return Enumerable.Range(0, k.Width * k.Height).Select(i => new Vector2Int(b.X + i % k.Width, b.Y + i / k.Width)); }).ToHashSet();

        [Test] public void TheRulesTableParsesAndRejectsOtherVersions()
        {
            var rules = EstateDecorRules.Load();
            Assert.That(rules.version, Is.EqualTo(EstateDecorRules.Version));
            Assert.Throws<InvalidOperationException>(() => EstateDecorRules.Parse("{\"version\":\"estate-decor-r0\"}"));
            Assert.That(rules.astra.seed, Is.EqualTo(20261024)); Assert.That(rules.astra.styleVersion, Is.EqualTo("art-v1"));
        }

        [Test] public void EveryRoleAndBuildingStageHasPackedArt()
        {
            var rules = EstateDecorRules.Load(); var art = EstateArt.Load();
            var roles = rules.astra.kits.SelectMany(k => k.props).Concat(new[] { "grass", "rocks" })
                .Concat(new[] { "fallow", "sage", "restored" }.SelectMany(s => Enumerable.Range(1, rules.astra.landVariants).Select(i => $"cluster-{s}-{i}")))
                .Concat(rules.trees.ids).Concat(rules.frontier.ids).Append(rules.frontier.edge).Append(rules.closedLand.deadTree).Concat(rules.closedLand.woods.SelectMany(w => w.ids))
                .Concat(rules.placed.SelectMany(p => new[] { p.id, p.rail, p.with })).Concat(new[] { rules.peasants.farm, rules.peasants.carry, rules.peasants.build })
                .Concat(new[] { rules.yardGround.well, rules.yardGround.house, rules.yardGround.other, rules.terrain.restored, rules.terrain.sage, rules.terrain.fallow, rules.fieldFenceCorner })
                .Where(r => !string.IsNullOrEmpty(r)).Distinct();
            foreach (var role in roles) Assert.That(art.Get(role), Is.Not.Null, role);
            foreach (var kind in rules.kindArt.Select(k => k.art).Distinct())
                for (var stage = 1; stage <= 3; stage++) { Assert.That(art.Body(kind, stage), Is.Not.Null, kind + stage); Assert.That(art.Shadow(kind, stage), Is.Not.Null); }
            Assert.That(art.BuildingPivot("manor", 1), Is.EqualTo(new Vector2(.5f, .125f)), "Astra's anchor (128, 280) on a 256x320 canvas.");
        }

        [Test] public void CoreLevelsPickTheThreeStageArt()
        {
            var rules = EstateDecorRules.Load();
            Assert.That(Enumerable.Range(0, 4).Select(l => rules.Stage("meta:granary", l)), Is.EqualTo(new[] { 1, 1, 2, 3 }));
            Assert.That(Enumerable.Range(0, 2).Select(l => rules.Stage("m3a:field", l)), Is.EqualTo(new[] { 1, 3 }), "A one-level site: ruin sketch, then the finished field.");
            var (config, catalog, state) = Estate(); var art = EstateArt.Load();
            var granary = state.Estate.Buildings.Single(b => b.Kind == "meta:granary");
            var plan = EstatePlanner.Plan(rules, art, config, state.Estate);
            var item = plan.Items.Single(i => i.BuildingId == granary.Id);
            Assert.That((item.BuildingArt, item.Stage), Is.EqualTo(("granary", 1)));
            Assert.That(item.Colour, Is.EqualTo(rules.Colour(rules.ruinState)), "A level-0 ruin is drawn in the fallow colour.");
            var started = AccountEstate.StartConstruction(catalog, config, state, 0, 0, granary.Id).State;
            item = EstatePlanner.Plan(rules, art, config, started.Estate).Items.Single(i => i.BuildingId == granary.Id);
            Assert.That(item.Stage, Is.EqualTo(rules.Stage("meta:granary", 1)), "Construction shows the target level's art, faded.");
            Assert.That(item.Colour.a, Is.EqualTo(rules.constructionAlpha));
            var done = AccountEstate.Advance(catalog, config, started, 60, 60).State;
            done = AccountEstate.StartConstruction(catalog, config, done, 60, 60, granary.Id).State;
            done = AccountEstate.Advance(catalog, config, done, 400, 400).State;
            Assert.That(done.Estate.Buildings.Single(b => b.Id == granary.Id).Level, Is.EqualTo(2));
            item = EstatePlanner.Plan(rules, art, config, done.Estate).Items.Single(i => i.BuildingId == granary.Id);
            Assert.That((item.Stage, item.Colour), Is.EqualTo((2, Color.white)));
        }

        [Test] public void RestorationColoursFollowTendedLand()
        {
            var rules = EstateDecorRules.Load();
            Assert.That(rules.Colour("restored"), Is.EqualTo(Color.white), "Tended land keeps the art's colours.");
            Assert.That(rules.TintOf("sage").saturation, Is.GreaterThan(rules.TintOf("fallow").saturation));
            Assert.That(rules.TintOf("closed").light, Is.EqualTo(1), "Closed land is darkened by its veil, not by the tint.");
            Assert.That(rules.TintOf("closed").saturation, Is.LessThan(rules.TintOf("sage").saturation));
            Assert.That(rules.closedLand.veil, Is.EqualTo(.7f).Within(.1f), "The blue-grey veil over closed land [가설].");
            var (config, _, state) = Estate();
            var plan = EstatePlanner.Plan(rules, EstateArt.Load(), config, state.Estate);
            var home = state.Estate.Buildings.Single(b => b.Kind == "m3a:home");
            Assert.That(plan.StateOf(new Vector2Int(home.X, home.Y - 1)), Is.EqualTo("restored"), "Next to the built home.");
            Assert.That(plan.StateOf(new Vector2Int(home.X - 5, home.Y - 1)), Is.EqualTo("sage"));
            Assert.That(plan.StateOf(new Vector2Int(21, 21)), Is.EqualTo("fallow"), "The far corner of the centre is untended.");
            Assert.That(plan.StateOf(new Vector2Int(45, 30)), Is.EqualTo("closed"));
            Assert.That(plan.StateOf(new Vector2Int(5, 5)), Is.Null, "Outside every zone.");
            foreach (var item in plan.Items.Where(i => i.Source == "frontier" || i.Source == "wood")) Assert.That(item.Colour, Is.EqualTo(rules.Colour("closed")), item.Key);
        }

        [Test] public void ThePlanIsDeterministicAndKeepsDecorationOffFootprintsAndRoads()
        {
            var (config, _, state) = Estate(); var rules = EstateDecorRules.Load(); var art = EstateArt.Load();
            string Key(EstateDrawItem i) => i.Key + i.Role + i.Grid.ToString("F4");
            var first = EstatePlanner.Plan(rules, art, config, state.Estate); var second = EstatePlanner.Plan(rules, art, config, state.Estate);
            Assert.That(second.Items.Select(Key), Is.EqualTo(first.Items.Select(Key)), "Same estate, same plan.");
            var footprints = Footprints(config, state.Estate);
            Assert.That(first.Road, Is.Not.Empty); Assert.That(first.Road.Overlaps(footprints), Is.False);
            foreach (var item in first.Items.Where(i => i.Source == "yard" || i.Source == "roadside" || i.Source == "empty-land"))
            {
                var cell = Vector2Int.FloorToInt(item.Grid);
                Assert.That(cell.x >= 20 && cell.y >= 20 && cell.x < 40 && cell.y < 40, Is.True, "on the open centre: " + item.Key);
                Assert.That(footprints.Contains(cell) && item.Source != "yard", Is.False, "no roadside or land prop on a footprint: " + item.Key);
            }
            // Closed land is painted under its veil (key art C): terrain, rivers, traces, dead trees and woods, all decided by seed.
            foreach (var item in first.In(EstateDrawLayer.Veiled))
                Assert.That(first.StateOf(Vector2Int.FloorToInt(item.Grid)), Is.EqualTo("closed"), "veiled art stays in closed land: " + item.Key);
            Assert.That(first.Items.Where(i => i.Source == "frontier" || i.Source == "wood").All(i => i.Layer == EstateDrawLayer.Veiled), Is.True);
            Assert.That(first.Items.Where(i => i.Source == "wood").All(i => i.Grid.x >= 20 && i.Grid.x < 40 && i.Grid.y < 20), Is.True, "Woods only in the north-east wood zone.");
            foreach (var zone in config.Zones.Where(z => z.Id != "zone:start"))
            {
                bool In(EstateDrawItem i) => i.Grid.x >= zone.X && i.Grid.x < zone.X + zone.Width && i.Grid.y >= zone.Y && i.Grid.y < zone.Y + zone.Height;
                Assert.That(first.Items.Count(i => i.Source == "terrain" && In(i)), Is.EqualTo(16), "Terrain patches cover " + zone.Id);
                Assert.That(first.In(EstateDrawLayer.Veiled).Count(In), Is.GreaterThan(5), "Traces or woods in " + zone.Id);
            }
            Assert.That(first.Items.Count(i => i.Source == "placed" && i.Grid.y > 40), Is.EqualTo(3), "The river runs on through the riverside zone.");
            Assert.That(first.Count("frontier"), Is.GreaterThan(0)); Assert.That(first.Count("wood"), Is.GreaterThan(0)); Assert.That(first.Count("frontier-edge"), Is.GreaterThan(0));
            TestContext.WriteLine("plan: " + string.Join(", ", first.Items.GroupBy(i => i.Source).Select(g => g.Key + "=" + g.Count())));
        }

        [Test] public void AMovedBuildingTakesItsAstraYardWithIt()
        {
            var (config, catalog, state) = Estate(); var rules = EstateDecorRules.Load(); var art = EstateArt.Load();
            var home = state.Estate.Buildings.Single(b => b.Kind == "m3a:home");
            var before = EstatePlanner.Plan(rules, art, config, state.Estate);
            var moved = AccountEstate.Move(catalog, config, state, 0, 0, home.Id, 22, 22);
            Assert.That(moved.Done, Is.True, moved.Reason);
            var after = EstatePlanner.Plan(rules, art, config, moved.State.Estate);
            var key = $"estate-{home.Id:00}";
            var a = before.Astra.Where(o => o.Owner == key).ToArray(); var b = after.Astra.Where(o => o.Owner == key).ToArray();
            var shift = EstateMapModel.Project(new Vector2(22 - home.X, 22 - home.Y));
            foreach (var o in b.Where(o => a.Any(p => p.Key == o.Key)))
            {
                var p = a.Single(q => q.Key == o.Key);
                Assert.That(o.Id, Is.EqualTo(p.Id)); Assert.That(o.X - p.X, Is.EqualTo(shift.x).Within(1e-3)); Assert.That(o.Y - p.Y, Is.EqualTo(shift.y).Within(1e-3));
            }
        }
    }
}
