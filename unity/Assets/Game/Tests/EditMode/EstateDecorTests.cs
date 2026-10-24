using System;
using System.IO;
using System.Linq;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    // #372: the data-driven estate decoration frame (rules table, deterministic planner).
    public sealed class EstateDecorTests
    {
        static EstateDecorRules Rules() => EstateDecorRules.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "Game/App/Resources", EstateDecorRules.ResourceName + ".json")));
        static (AccountEstateConfig Config, MetaCatalog Catalog, MetaState State) Estate()
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var config = CanonicalContent.CreateAccountEstateConfig();
            return (config, catalog, AccountEstate.NewPrototype(catalog, config, 0, 0));
        }
        static string Key(EstateDecorItem i) => i.Source + ":" + i.Owner + ":" + i.Role + "@" + i.Grid.x.ToString("F4") + "," + i.Grid.y.ToString("F4");

        [Test] public void TheRulesTableParsesAndRejectsOtherVersions()
        {
            var rules = Rules();
            Assert.That(rules.version, Is.EqualTo(EstateDecorRules.Version));
            Assert.That(rules.Roles().All(r => r.StartsWith("fallow.prop.", StringComparison.Ordinal)), Is.True, "Temporary art is the fallow field props only.");
            Assert.Throws<InvalidOperationException>(() => EstateDecorRules.Parse("{\"version\":\"estate-decor-r9\"}"));
        }

        [Test] public void ThePlanIsDeterministicAndKeepsOffFootprintsAndRoads()
        {
            var (config, _, state) = Estate(); var rules = Rules();
            var first = EstateDecorPlanner.Plan(rules, config, state.Estate); var second = EstateDecorPlanner.Plan(rules, config, state.Estate);
            Assert.That(second.Select(Key), Is.EqualTo(first.Select(Key)), "Same layout, same decoration.");
            var footprints = state.Estate.Buildings.SelectMany(b => Enumerable.Range(0, 4).Select(i => new Vector2Int(b.X + i % 2, b.Y + i / 2))).ToHashSet();
            var road = first.Where(i => i.Source == EstateDecorSource.Road).Select(i => Vector2Int.FloorToInt(i.Grid)).ToHashSet();
            Assert.That(road, Is.Not.Empty); Assert.That(road.Overlaps(footprints), Is.False, "Roads go around buildings.");
            var home = state.Estate.Buildings.Single(b => b.Kind == "m3a:home");
            Assert.That(road, Does.Contain(new Vector2Int(home.X + 1, home.Y + 2)), "Roads start at the home door.");
            foreach (var item in first.Where(i => i.Source != EstateDecorSource.Road))
            {
                var cell = Vector2Int.FloorToInt(item.Grid);
                Assert.That(cell.x >= 20 && cell.y >= 20 && cell.x < 40 && cell.y < 40, Is.True, "Decoration stays on the open centre zone (#374): " + Key(item));
                Assert.That(footprints.Contains(cell), Is.False, "No decoration on a footprint: " + Key(item));
                Assert.That(road.Contains(cell), Is.False, "No decoration on a road: " + Key(item));
            }
            var clusters = first.Where(i => i.Source == EstateDecorSource.Cluster).GroupBy(i => i.Owner).ToArray();
            Assert.That(clusters.Length, Is.InRange(1, rules.clusters.count));
            Assert.That(clusters.All(g => g.Count() >= rules.clusters.minSize && g.Count() <= rules.clusters.maxSize), Is.True,
                "Every cluster reaches its size range when free cells remain (review on #373): " + string.Join(",", clusters.Select(g => g.Count())));
            TestContext.WriteLine("plan: " + string.Join(", ", first.GroupBy(i => i.Source).Select(g => g.Key + "=" + g.Count())));
        }

        [Test] public void AMovedBuildingTakesItsYardAndLeavesDistantYardsAlone()
        {
            var (config, catalog, state) = Estate(); var rules = Rules();
            var forge = state.Estate.Buildings.Single(b => b.Kind == "meta:forge");
            var scriptorium = state.Estate.Buildings.Single(b => b.Kind == "meta:scriptorium");
            var before = EstateDecorPlanner.Plan(rules, config, state.Estate);
            var moved = AccountEstate.Move(catalog, config, state, 0, 0, forge.Id, 28, 26);
            Assert.That(moved.Done, Is.True, moved.Reason);
            var after = EstateDecorPlanner.Plan(rules, config, moved.State.Estate);
            Assert.That(after.Where(i => i.Owner == scriptorium.Id && i.Source == EstateDecorSource.Yard).Select(Key),
                Is.EqualTo(before.Where(i => i.Owner == scriptorium.Id && i.Source == EstateDecorSource.Yard).Select(Key)), "A yard far from the move is unchanged.");
            var yard = after.Where(i => i.Owner == forge.Id && i.Source == EstateDecorSource.Yard).ToArray();
            Assert.That(yard, Is.Not.Empty);
            foreach (var item in yard)
            {
                var cell = Vector2Int.FloorToInt(item.Grid);
                Assert.That(cell.x >= 27 && cell.x <= 30 && cell.y >= 25 && cell.y <= 28, Is.True, "The forge yard follows it to (28, 26): " + Key(item));
            }
            Assert.That(after.Select(Key), Is.Not.EqualTo(before.Select(Key)), "Moving a building re-places the decoration.");
        }

        [Test] public void EveryRuleRoleHasArtInTheWaveCatalog()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("The fallow props ship with the wave-1a art.");
            var catalog = ArtCatalog.Load();
            foreach (var role in Rules().Roles()) Assert.That(catalog.ResolveRole(role).Sprite, Is.Not.Null, role);
        }
    }
}
