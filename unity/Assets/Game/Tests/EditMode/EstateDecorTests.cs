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
        static (AccountEstateConfig Config, MetaCatalog Catalog, MetaState State) Estate(string version = null)
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var config = CanonicalContent.CreateAccountEstateConfig(version);
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
                .Concat(rules.trees.ids).Concat(rules.frontier.ids).Append(rules.sites.ground).Append(rules.closedLand.deadTree).Concat(rules.closedLand.woods.SelectMany(w => w.ids))
                .Concat(rules.placed.SelectMany(p => new[] { p.id, p.rail, p.with })).Concat(new[] { rules.peasants.farm, rules.peasants.carry, rules.peasants.build })
                .Concat(new[] { rules.yardGround.well, rules.yardGround.house, rules.yardGround.other, rules.terrain.restored, rules.terrain.sage, rules.terrain.fallow, rules.fieldFenceCorner })
                .Where(r => !string.IsNullOrEmpty(r)).Distinct();
            foreach (var role in roles) Assert.That(art.Get(role), Is.Not.Null, role);
            foreach (var kind in rules.kindArt.Select(k => k.art).Distinct())
                for (var stage = 1; stage <= 3; stage++) { Assert.That(art.Body(kind, stage), Is.Not.Null, kind + stage); Assert.That(art.Shadow(kind, stage), Is.Not.Null); }
            Assert.That(art.BuildingPivot("manor", 1), Is.EqualTo(new Vector2(.5f, .125f)), "Astra's anchor (128, 280) on a 256x320 canvas.");
            Assert.That(art.BodyWidth("house", 1), Is.EqualTo(90), "Astra scales bodies by the trimmed opaque width (assembly.js bodyWidths), not drawRect.");
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
            Assert.That(item.Colour, Is.EqualTo(Color.white), "The authored level-0 start-stage art keeps its original colours inside the light.");
            var home = state.Estate.Buildings.Single(b => b.Kind == "m3a:home");
            var homeArt = plan.Items.Single(i => i.BuildingId == home.Id);
            Assert.That((homeArt.Stage, homeArt.Colour), Is.EqualTo((2, Color.white)), "The starting home is complete, not the ruined manor stage.");
            Assert.That(plan.Lamps.Any(l => l.Item1 == new Vector2(home.X + 1, home.Y + 1) && l.Item2 == rules.night.homeLampCells), Is.True);
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
            // #379: art under the night is cooler and greyer than any open state, so no day ochre shows through.
            Assert.That(rules.TintOf("closed").saturation, Is.LessThan(rules.TintOf("fallow").saturation));
            Assert.That(rules.TintOf("closed").light, Is.LessThan(1), "Part of the M2 darkness formula's blue-grey.");
            var night = rules.night;
            Assert.That(night.Alpha(-1), Is.EqualTo(0), "Open land is fully lit.");
            Assert.That(night.Alpha(0), Is.EqualTo(.5f).Within(1e-4f), "Half dark at the lit edge [가설].");
            Assert.That(night.Alpha(night.fullCells), Is.EqualTo(.75f).Within(1e-4f)); Assert.That(night.Alpha(20), Is.EqualTo(.75f).Within(1e-4f), "Far night 0.75 (user decision 2026-10-25).");
            for (var d = -1f; d < 4; d += .25f) Assert.That(night.Alpha(d + .25f), Is.GreaterThanOrEqualTo(night.Alpha(d)), "The night only deepens away from light.");
            // Keep the broad r5 land for the sage-distance case; the compact r6 start has less untended space.
            var (config, _, state) = Estate("m3a-prototype-r5");
            var plan = EstatePlanner.Plan(rules, EstateArt.Load(), config, state.Estate);
            var home = state.Estate.Buildings.Single(b => b.Kind == "m3a:home");
            Assert.That(plan.StateOf(new Vector2Int(home.X, home.Y - 1)), Is.EqualTo("restored"), "Next to the built home.");
            Assert.That(plan.StateOf(new Vector2Int(home.X - 5, home.Y - 1)), Is.EqualTo("sage"));
            Assert.That(plan.StateOf(new Vector2Int(21, 21)), Is.EqualTo("closed"), "r5: the old centre's corner is outskirts, outside the lit start.");
            Assert.That(plan.StateOf(new Vector2Int(45, 30)), Is.EqualTo("closed"));
            Assert.That(plan.StateOf(new Vector2Int(45, 10)), Is.Null, "Outside every zone.");
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
                Assert.That(config.Zones.Any(z => state.Estate.OpenZones.Contains(z.Id) && cell.x >= z.X && cell.y >= z.Y && cell.x < z.X + z.Width && cell.y < z.Y + z.Height), Is.True, "on the open centre: " + item.Key);
                Assert.That(footprints.Contains(cell) && item.Source != "yard", Is.False, "no roadside or land prop on a footprint: " + item.Key);
            }
            // Land outside the open zones is painted under the night (key art C): terrain, rivers, traces, dead trees and woods.
            foreach (var item in first.In(EstateDrawLayer.Veiled))
                Assert.That(first.StateOf(Vector2Int.FloorToInt(item.Grid)), Is.EqualTo("closed").Or.Null, "the land to come stays out of open land: " + item.Key);
            foreach (var zone in config.Zones)
                Assert.That(first.Area.Contains(new Vector2Int(zone.X - rules.night.margin, zone.Y - rules.night.margin)) && first.Area.Contains(new Vector2Int(zone.X + zone.Width + rules.night.margin - 1, zone.Y + zone.Height + rules.night.margin - 1)), Is.True, "painted area covers " + zone.Id);
            // #379: roads and unbuilt sites stand on Astra's earth, built buildings in open land carry a lamp.
            Assert.That(first.Count("road"), Is.EqualTo(first.Road.Count)); Assert.That(first.Items.Where(i => i.Source == "road").All(i => i.Role == rules.sites.ground), Is.True);
            Assert.That(first.Count("site"), Is.EqualTo(state.Estate.Buildings.Count(b => b.Level == 0)));
            Assert.That(first.Lamps.Count, Is.EqualTo(state.Estate.Buildings.Count(b => b.Level > 0)));
            Assert.That(first.Items.Where(i => i.Source == "frontier" || i.Source == "wood").All(i => i.Layer == EstateDrawLayer.Veiled), Is.True);
            bool In(AccountEstateZone zone, Vector2 grid) => grid.x >= zone.X && grid.x < zone.X + zone.Width && grid.y >= zone.Y && grid.y < zone.Y + zone.Height;
            var wood = config.Zones.Single(z => z.Id == "zone:inner-northeast-wood");
            Assert.That(first.Items.Where(i => i.Source == "wood").All(i => In(wood, i.Grid)), Is.True, "Woods only in the north-east wood zone.");
            foreach (var zone in config.Zones.Where(z => z.Id != "zone:start"))
            {
                Assert.That(first.Items.Count(i => i.Source == "terrain" && In(zone, i.Grid)), Is.GreaterThan(0), "Terrain patches cover " + zone.Id);
                if (zone.Width * zone.Height >= 200) Assert.That(first.In(EstateDrawLayer.Veiled).Count(i => In(zone, i.Grid)), Is.GreaterThan(0), "Traces or woods in " + zone.Id);
            }
            Assert.That(first.In(EstateDrawLayer.Veiled).Count(), Is.GreaterThan(30));
            // The river runs down the estate's right edge (key art A): from the north-east wood to the riverside zone.
            var river = first.Items.Where(i => i.Source == "placed" && i.Role.StartsWith("river", StringComparison.Ordinal)).OrderBy(i => i.Grid.x + i.Grid.y).ToArray();
            Assert.That(river.Length, Is.EqualTo(7));
            Assert.That(river.All(i => i.Grid.x - i.Grid.y >= 10), Is.True, "every river patch is on the right side");
            Assert.That(In(wood, river.First().Grid) && In(config.Zones.Single(z => z.Id == "zone:inner-riverside"), river.Last().Grid), Is.True);
            Assert.That(first.Count("frontier"), Is.GreaterThan(0)); Assert.That(first.Count("wood"), Is.GreaterThan(0)); Assert.That(first.Count("frontier-edge"), Is.EqualTo(0), "No ink edge smudges (#379).");
            TestContext.WriteLine("plan: " + string.Join(", ", first.Items.GroupBy(i => i.Source).Select(g => g.Key + "=" + g.Count())));
        }

        [Test] public void LargeLandmarksHaveFortyPercentSilhouettesButSmallPropsStayUnderTheNight()
        {
            var (config, _, state) = Estate(); var rules = EstateDecorRules.Load();
            var plan = EstatePlanner.Plan(rules, EstateArt.Load(), config, state.Estate);
            var landmarks = plan.In(EstateDrawLayer.Landmark).ToArray();
            Assert.That(landmarks, Is.Not.Empty);
            Assert.That(rules.night.landmarkBrightness, Is.EqualTo(.4f).Within(1e-5f));
            foreach (var item in landmarks)
            {
                Assert.That(item.Source, Is.EqualTo("landmark"));
                Assert.That(item.Colour.g, Is.EqualTo(.4f).Within(1e-5f));
                Assert.That(item.Colour.b, Is.EqualTo(1), "Brightness is applied once, without the extra closed-land darkness formula.");
                Assert.That(plan.StateOf(Vector2Int.FloorToInt(item.Grid)), Is.EqualTo("closed").Or.Null);
            }
            foreach (var river in plan.Items.Where(i => i.Source == "placed" && i.Role.StartsWith("river", StringComparison.Ordinal)))
                Assert.That(landmarks.Any(i => i.Role == river.Role && i.Grid == river.Grid), Is.True, "The closed river stays legible: " + river.Key);
            Assert.That(landmarks.Any(i => i.Role.StartsWith("bridge", StringComparison.Ordinal)), Is.True);
            Assert.That(landmarks.Any(i => i.Role == "frontier-ruin"), Is.True);
            Assert.That(landmarks.Any(i => i.Role == "frontier-dead-tree" || i.Role == "frontier-broken-fence"), Is.False,
                "Small repeated traces stay beneath the night; only the authored large fence kits rise above it.");
            foreach (var item in plan.Items.Where(i => i.Role == "frontier-empty-armor" || i.Role == "frontier-stump-tracks"))
                Assert.That(item.Layer, Is.EqualTo(EstateDrawLayer.Veiled), "Small traces remain under the night.");
            Assert.That(landmarks.Any(i => i.Source == "roadside" || i.Role == "grass" || i.Role == "rocks"), Is.False);
        }

        // #379: one night by distance from open land — none inside, half at the edge, full a few cells out, and the same on both
        // sides of a boundary between two closed zones (no straight seam).
        [Test] public void TheNightDeepensWithDistanceFromOpenLandAcrossClosedZones()
        {
            var (config, _, state) = Estate(); var rules = EstateDecorRules.Load(); var plan = EstatePlanner.Plan(rules, EstateArt.Load(), config, state.Estate);
            var open = config.Zones.Where(z => state.Estate.OpenZones.Contains(z.Id)).Select(z => new RectInt(z.X, z.Y, z.Width, z.Height)).ToArray();
            var field = EstateNight.Field(rules.night, plan.Area, open); var res = rules.night.texelsPerCell;
            Assert.That(field.width, Is.EqualTo(plan.Area.width * res)); Assert.That(field.height, Is.EqualTo(plan.Area.height * res));
            float At(float u, float v) => field.GetPixel(Mathf.FloorToInt((u - plan.Area.xMin) * res), Mathf.FloorToInt((v - plan.Area.yMin) * res)).a;
            Assert.That(At(30, 30), Is.EqualTo(0).Within(.01f), "The open centre is lit.");
            Assert.That(At(34.6f, 30), Is.InRange(.5f, .75f), "Just past the r6 lit edge, in the east outskirts: between half and full.");
            Assert.That(At(55, 50), Is.EqualTo(.75f).Within(.01f), "Deep in the land to come, full night.");
            // North and west outskirts meet at v=26; both samples remain about 1.5 cells from the light.
            Assert.That(At(24.5f, 25.6f), Is.EqualTo(At(24.5f, 26.4f)).Within(.02f), "No seam between closed zones.");
            Assert.That(At(24.5f, 26.4f), Is.InRange(.6f, .74f), "1.5 cells from light: still rising toward full night.");
            var c = field.GetPixel(0, 0); Assert.That(new Color(c.r, c.g, c.b), Is.EqualTo((Color)rules.night.Colour).Using(ColorComparer));
            Assert.That(c.a, Is.EqualTo(1).Within(.01f), "The painted area's edge closes to opaque night, matching the page behind it.");
        }
        static readonly IEqualityComparer<Color> ColorComparer = new ToleranceColour();
        sealed class ToleranceColour : IEqualityComparer<Color>
        {
            public bool Equals(Color a, Color b) => Mathf.Abs(a.r - b.r) < .01f && Mathf.Abs(a.g - b.g) < .01f && Mathf.Abs(a.b - b.b) < .01f;
            public int GetHashCode(Color c) => 0;
        }

        [Test] public void AMovedBuildingTakesItsAstraYardWithIt()
        {
            var (config, catalog, state) = Estate(); var rules = EstateDecorRules.Load(); var art = EstateArt.Load();
            var home = state.Estate.Buildings.Single(b => b.Kind == "m3a:home");
            var before = EstatePlanner.Plan(rules, art, config, state.Estate);
            var moved = AccountEstate.Move(catalog, config, state, 0, 0, home.Id, 30, 30);
            Assert.That(moved.Done, Is.True, moved.Reason);
            var after = EstatePlanner.Plan(rules, art, config, moved.State.Estate);
            var key = $"estate-{home.Id:00}";
            var a = before.Astra.Where(o => o.Owner == key).ToArray(); var b = after.Astra.Where(o => o.Owner == key).ToArray();
            var shift = EstateMapModel.Project(new Vector2(30 - home.X, 30 - home.Y));
            foreach (var o in b.Where(o => a.Any(p => p.Key == o.Key)))
            {
                var p = a.Single(q => q.Key == o.Key);
                Assert.That(o.Id, Is.EqualTo(p.Id)); Assert.That(o.X - p.X, Is.EqualTo(shift.x).Within(1e-3)); Assert.That(o.Y - p.Y, Is.EqualTo(shift.y).Within(1e-3));
            }
        }
    }
}
