using System.Collections.Generic;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class EstateTrunkRoadTests
    {
        static readonly Vector2Int[] Steps = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        [Test]
        public void R7TrunkCrossesPlazaAndReachesBothEdgesWithoutCyclesOrDuplicateParallelRoutes()
        {
            var config = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            var state = AccountEstate.NewPrototype(CanonicalContent.CreateMetaCatalog(), config, 0, 0);
            var plan = EstatePlanner.Plan(EstateDecorRules.Load(), EstateArt.Load(), config, state.Estate);
            var road = plan.Road;
            Assert.That(road, Does.Contain(new Vector2Int(29, 29)), "The home-front plaza is on the trunk.");
            Assert.That(road.Any(c => c.x == 24), Is.True, "West end.");
            Assert.That(road.Any(c => c.x == 35), Is.True, "East bridge direction.");
            var edges = road.Sum(c => Steps.Count(step => road.Contains(c + step))) / 2;
            Assert.That(edges, Is.EqualTo(road.Count - 1), "A connected cell tree has no loops or redundant side-by-side paths.");
            foreach (var cell in road)
                Assert.That(EstatePlanner.RoadPath(new Vector2Int(29, 29), cell, road.Contains), Is.Not.Empty);
            foreach (var building in state.Estate.Buildings)
            {
                var kind = config.Buildings.Single(k => k.Kind == building.Kind);
                var door = new Vector2Int(building.X + kind.Width - 1, building.Y + kind.Height);
                Assert.That(road, Does.Contain(door), building.Kind);
                for (var x = building.X; x < building.X + kind.Width; x++)
                    for (var y = building.Y; y < building.Y + kind.Height; y++)
                        Assert.That(road, Does.Not.Contain(new Vector2Int(x, y)), building.Kind);
            }
            var second = EstatePlanner.Plan(EstateDecorRules.Load(), EstateArt.Load(), config, state.Estate);
            Assert.That(second.Road, Is.EquivalentTo(road));
            Assert.That(plan.Items.Where(i => i.Source == "road").All(i => i.IsometricRoad && i.Role.StartsWith("road-")), Is.True);
            Assert.That(plan.Items.Count(i => i.Source == "road"), Is.EqualTo(road.Count));
        }

        [Test]
        public void EntranceSpursJoinTheNearestTrunkInsteadOfReturningIndependentlyToHome()
        {
            var area = new HashSet<Vector2Int>();
            for (var y = 0; y < 9; y++) for (var x = 0; x < 13; x++) area.Add(new Vector2Int(x, y));
            var routes = EstatePlanner.TrunkRoads(new Vector2Int(6, 4),
                new[] { new Vector2Int(2, 1), new Vector2Int(10, 7) }, area);
            Assert.That(routes[3], Is.EqualTo(new[] { new Vector2Int(2, 4), new Vector2Int(2, 3), new Vector2Int(2, 2), new Vector2Int(2, 1) }));
            Assert.That(routes[4], Is.EqualTo(new[] { new Vector2Int(10, 4), new Vector2Int(10, 5), new Vector2Int(10, 6), new Vector2Int(10, 7) }));
        }

        [Test]
        public void RoadArtUsesBendsAndJunctionsOnlyWhereTheNetworkTurnsOrBranches()
        {
            var centre = Vector2Int.zero;
            Assert.That(EstatePlanner.RoadStyle(centre, new HashSet<Vector2Int> { centre, Vector2Int.left, Vector2Int.right }).Role, Is.EqualTo("road-straight"));
            Assert.That(EstatePlanner.RoadStyle(centre, new HashSet<Vector2Int> { centre, Vector2Int.left, Vector2Int.down }).Role, Is.EqualTo("road-bend"));
            Assert.That(EstatePlanner.RoadStyle(centre, new HashSet<Vector2Int> { centre, Vector2Int.left, Vector2Int.right, Vector2Int.up }).Role, Is.EqualTo("road-fork"));
        }
    }
}
