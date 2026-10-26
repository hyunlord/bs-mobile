using System.Collections.Generic;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.UI;

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
                        Assert.That(road, Has.No.Member(new Vector2Int(x, y)), building.Kind);
            }
            var second = EstatePlanner.Plan(EstateDecorRules.Load(), EstateArt.Load(), config, state.Estate);
            Assert.That(second.Road, Is.EquivalentTo(road));
            Assert.That(plan.Items.Where(i => i.Source == "road").All(i => i.IsometricRoad && i.Role.StartsWith("road-")), Is.True);
            var ribbons = plan.Items.Where(i => i.Source == "road").ToArray();
            Assert.That(ribbons.Length, Is.LessThan(road.Count), "One ribbon per route, never one quad per cell.");
            Assert.That(ribbons.All(i => i.RoadPoints != null && i.RoadPoints.Length >= 2), Is.True);
            Assert.That(ribbons.Count(i => Mathf.Abs(i.RoadWidth * (390f / 768f) - 18) < .001f), Is.EqualTo(2), "Two trunk halves meet at the plaza.");
            Assert.That(ribbons.Where(i => i.RoadWidth * (390f / 768f) < 17).All(i => Mathf.Abs(i.RoadWidth * (390f / 768f) - 11) < .001f), Is.True);
            Assert.That(ribbons.Any(i => Vector2.Distance(i.RoadPoints[i.RoadPoints.Length - 1], new Vector2(35, 23.8f)) < .001f), Is.True,
                "The eastern trunk reaches the authored bridge anchor, not merely the open-land edge.");
            foreach (var ribbon in ribbons)
            {
                var curve = EstateTerrainImage.RoundedRoad(ribbon.RoadPoints);
                for (var i = 0; i < curve.Count; i++)
                {
                    var tangent = (EstateMapModel.Project(curve[Mathf.Min(curve.Count - 1, i + 1)]) -
                        EstateMapModel.Project(curve[Mathf.Max(0, i - 1)])).normalized;
                    var normal = EstateMapModel.Unproject(new Vector2(-tangent.y, tangent.x));
                    var left = EstateTerrainImage.RoadSideExtent(curve[i], -normal, ribbon.RoadWidth * .5f, ribbon.RoadFootprints);
                    var right = EstateTerrainImage.RoadSideExtent(curve[i], normal, ribbon.RoadWidth * .5f, ribbon.RoadFootprints);
                    Assert.That(left + right, Is.GreaterThan(1), ribbon.Key + " must not collapse between site foundations.");
                }
            }
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
        public void RibbonMeshUsesItsRectCentreAndOnlyTheAuthoredDirtBand()
        {
            var canvas = new GameObject("Road mesh test", typeof(Canvas));
            var owner = new GameObject("Ribbon", typeof(RectTransform), typeof(CanvasRenderer), typeof(EstateTerrainImage));
            var texture = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 512, 512), new Vector2(.5f, .5f));
            try
            {
                owner.transform.SetParent(canvas.transform, false);
                var graphic = owner.GetComponent<EstateTerrainImage>();
                graphic.rectTransform.pivot = new Vector2(0, 1);
                graphic.rectTransform.sizeDelta = new Vector2(128, 64);
                graphic.sprite = sprite; graphic.Grid = Vector2.zero;
                graphic.PaintedWidth = 128; graphic.RoadWidth = 18;
                graphic.RoadPoints = new[] { new Vector2(-1, 0), new Vector2(1, 0) };
                graphic.canvasRenderer.cull = false;
                graphic.Rebuild(CanvasUpdate.PreRender);
                var mesh = graphic.canvasRenderer.GetMesh();
                Assert.That(mesh.vertexCount, Is.GreaterThan(0));
                var firstCentre = mesh.vertices[4];
                Assert.That(firstCentre.x, Is.EqualTo(32).Within(.001f));
                Assert.That(firstCentre.y, Is.EqualTo(-16).Within(.001f), "Top-left RectTransform pivot cannot shift the road off its grid path.");
                Assert.That(mesh.uv.Min(uv => uv.y), Is.EqualTo(.455f).Within(.001f));
                Assert.That(mesh.uv.Max(uv => uv.y), Is.EqualTo(.545f).Within(.001f));
                Assert.That(Vector3.Distance(mesh.vertices[0], mesh.vertices[8]), Is.EqualTo(18).Within(.001f), "Changing the sampled dirt band must not enlarge the footprint boundary.");
                Assert.That(Vector3.Distance(mesh.vertices[0], mesh.vertices[1]), Is.EqualTo(18 * .04f).Within(.001f));
                Assert.That(Vector3.Distance(mesh.vertices[7], mesh.vertices[8]), Is.EqualTo(18 * .04f).Within(.001f));
                Assert.That(mesh.colors32[0].a, Is.Zero);
                Assert.That(mesh.colors32[1].a, Is.EqualTo(255), "Only the outer lane fades; broad grass shoulders no longer consume the road width.");
            }
            finally
            {
                Object.DestroyImmediate(owner); Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void WideTrunkFeathersBeforeAdjacentSiteRatherThanPaintingItsFoundation()
        {
            var footprints = new[] { new RectInt(2, 2, 2, 2) };
            var centre = new Vector2(1.5f, 2.5f);
            var direction = new Vector2(.04f, 0);
            Assert.That(EstateTerrainImage.RoadSideExtent(centre, direction, 18, footprints), Is.EqualTo(12.3f).Within(.001f));
            Assert.That(EstateTerrainImage.RoadSideExtent(centre, -direction, 18, footprints), Is.EqualTo(18));
        }

        [Test]
        public void RoundedRibbonKeepsEndpointsAndIsometricAxesWithoutSharpCellCorners()
        {
            var input = new[] { new Vector2(1, 1), new Vector2(2, 1), new Vector2(3, 1), new Vector2(3, 2), new Vector2(3, 3) };
            var curve = EstateTerrainImage.RoundedRoad(input);
            Assert.That(curve[0], Is.EqualTo(input[0]));
            Assert.That(curve[curve.Count - 1], Is.EqualTo(input[input.Length - 1]));
            Assert.That(curve, Has.No.Member(new Vector2(3, 1)), "Round the elbow instead of drawing a staircase corner.");
            Assert.That(curve.Any(p => p.x > 2.7f && p.x < 3 && p.y > 1 && p.y < 1.3f), Is.True);
            var diagonal = EstateMapModel.Project(curve[1]) - EstateMapModel.Project(curve[0]);
            Assert.That(Mathf.Abs(diagonal.x), Is.GreaterThan(0));
            Assert.That(Mathf.Abs(diagonal.y), Is.GreaterThan(0), "The grid axis is diagonal on screen.");
            Assert.That(curve.All(p => p.x >= 1 && p.x <= 3 && p.y >= 1 && p.y <= 3), Is.True);
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
