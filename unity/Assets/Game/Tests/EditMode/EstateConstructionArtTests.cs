using System;
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
    public sealed class EstateConstructionArtTests
    {
        [TestCase(100, 1)] [TestCase(68, 1)] [TestCase(67, 2)]
        [TestCase(35, 2)] [TestCase(34, 3)] [TestCase(1, 3)] [TestCase(0, 3)]
        public void ProgressCrossesOnlyTheApprovedThirtyThreeAndSixtySixPercentBoundaries(int remaining, int expected) =>
            Assert.That(EstateConstructionArt.Stage(new MetaEstateWork(1, 1, 100, remaining)), Is.EqualTo(expected));

        [Test] public void EveryApprovedStageAndWorkerFrameKeepsTheAuthoredGroundAnchor()
        {
            var art = EstateArt.Load();
            for (var stage = 1; stage <= 3; stage++)
                foreach (var field in new[] { false, true }) foreach (var lit in new[] { false, true })
                {
                    var role = EstateConstructionArt.Role(field, stage, lit);
                    Assert.That(art.Get(role).rect.size, Is.EqualTo(new Vector2(136, 160)));
                    Assert.That(art.Pivot(role).x, Is.EqualTo(.5f));
                    Assert.That(art.Pivot(role).y, Is.EqualTo(.225f).Within(.00001f));
                }
            for (var frame = 0; frame < 16; frame++)
                foreach (var direction in new[] { "se", "sw" })
                {
                    var role = "worker-carry-" + direction + "-" + frame.ToString("00");
                    Assert.That(art.Get(role).rect.size, Is.EqualTo(new Vector2(256, 256)), "Full cell, no obsolete 4px inset.");
                    Assert.That(art.Pivot(role), Is.EqualTo(new Vector2(direction == "se" ? 122f / 256 : 134f / 256, 25f / 256)));
                }
        }

        [Test] public void ConstructionPlanUsesSeparateFieldStagesAndOnlyRoadCellsForItsCarrier()
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var config = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            var state = AccountEstate.NewPrototype(catalog, config, 0, 0);
            var granary = state.Estate.Buildings.Single(b => b.Kind == "meta:granary");
            state = AccountEstate.StartConstruction(catalog, config, state, 0, 0, granary.Id).State;
            state = AccountEstate.Advance(catalog, config, state, 60, 60).State;
            var field = state.Estate.Buildings.Single(b => b.Kind == "m3a:field");
            state = AccountEstate.StartConstruction(catalog, config, state, 60, 60, field.Id).State;
            Assert.That(state.Estate.Active?.BuildingId, Is.EqualTo(field.Id));
            var rules = EstateDecorRules.Load(); var art = EstateArt.Load();
            var plan = EstatePlanner.Plan(rules, art, config, state.Estate);
            Assert.That(plan.Items.Single(i => i.SiteId == field.Id).Role, Is.EqualTo("construction-field-1-lit"));
            Assert.That(plan.CarryRoute.Length, Is.GreaterThan(1));
            Assert.That(plan.CarryRoute.All(p => plan.Road.Contains(Vector2Int.FloorToInt(p))), Is.True);
            var again = EstatePlanner.Plan(rules, art, config, state.Estate);
            Assert.That(again.CarryRoute, Is.EqualTo(plan.CarryRoute));
            Assert.That(again.Items.Where(i => i.Source == "construction-prop").Select(i => (i.Role, i.Grid)),
                Is.EqualTo(plan.Items.Where(i => i.Source == "construction-prop").Select(i => (i.Role, i.Grid))));
            Assert.That(plan.Items.Where(i => i.Source == "construction-prop").All(i => i.Grid.y >= field.Y + 2), Is.True);
        }

        [Test] public void WorkerRepeatsItsRoadRoundTripWithoutChangingThePlan()
        {
            var points = new[] { Vector2.zero, Vector2.right, Vector2.one };
            var route = new EstateCarryRoute(points);
            Assert.That(route.Sample(0).Grid, Is.EqualTo(points[0]));
            Assert.That(Vector2.Distance(route.Sample(route.Length / EstateCarryRoute.PixelsPerSecond).Grid, points[2]), Is.LessThan(.0001f));
            Assert.That(Vector2.Distance(route.Sample(2 * route.Length / EstateCarryRoute.PixelsPerSecond).Grid, points[0]), Is.LessThan(.0001f));
            Assert.That(route.Sample(.75).Frame, Is.EqualTo(15));
            Assert.That(route.Sample(.8).Frame, Is.Zero);
            Assert.That(points, Is.EqualTo(new[] { Vector2.zero, Vector2.right, Vector2.one }));
        }

        [Test] public void TimeRefreshRebuildsTheCachedPlanOnlyWhenConstructionStageChanges()
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var config = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            var state = AccountEstate.NewPrototype(catalog, config, 0, 0); double now = 0;
            var screen = new EstateMapScreen(catalog, config, state, realtime: () => now);
            Assert.That(screen.StartWork(state.Estate.Buildings.Single(b => b.Kind == "meta:granary").Id), Is.True);
            var rules = EstateDecorRules.Load(); var art = EstateArt.Load();
            var first = screen.Plan(rules, art);
            now = 1; screen.Advance();
            Assert.That(screen.Plan(rules, art), Is.SameAs(first));
            now = 20; Assert.That(screen.Advance(), Is.True);
            var second = screen.Plan(rules, art); Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Items.Single(i => i.Source == "construction").Role, Is.EqualTo("construction-building-2-lit"));
            now = 40; Assert.That(screen.Advance(), Is.True);
            Assert.That(screen.Plan(rules, art).Items.Single(i => i.Source == "construction").Role, Is.EqualTo("construction-building-3-lit"));
        }

        [Test] public void RiverWaterAndApprovedBanksReplaceBakedRoundStoneOutlines()
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var config = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            var state = AccountEstate.NewPrototype(catalog, config, 0, 0);
            var plan = EstatePlanner.Plan(EstateDecorRules.Load(), EstateArt.Load(), config, state.Estate);
            Assert.That(plan.Items.Where(i => i.Role == "riverbank-patch" || i.Role == "river-bend-patch").All(i => i.WaterOnly), Is.True);
            Assert.That(plan.Items.Count(i => i.Source == "river-bank"), Is.GreaterThan(0));
            foreach (var bank in plan.Items.Where(i => i.Source == "river-bank"))
            {
                Assert.That(EstateTerrainImage.IsDarkBank(bank), Is.EqualTo(bank.Role.EndsWith("-dark", StringComparison.Ordinal)));
                Assert.That(bank.Colour, Is.EqualTo(Color.white), "Authored bank art keeps neutral restoration parameters.");
            }
            Assert.That(plan.Items.Where(i => i.Source == "wood").All(i => i.Role.StartsWith("estate-tree-", StringComparison.Ordinal)), Is.True);
        }

        [TestCase("bank-straight-01-dark", "river-bank", EstateDrawLayer.Landmark, true)]
        [TestCase("bank-bridge-02-dark", "river-bank", EstateDrawLayer.Landmark, true)]
        [TestCase("bank-straight-01-lit", "river-bank", EstateDrawLayer.Standing, false)]
        [TestCase("bank-straight-01-dark", "river-bank", EstateDrawLayer.Veiled, false)]
        [TestCase("riverbank-patch", "landmark", EstateDrawLayer.Landmark, false)]
        [TestCase("estate-tree-conifer-01-dark", "wood", EstateDrawLayer.Veiled, false)]
        public void DisplayBrightnessLiftIsExclusiveToApprovedAboveNightDarkBanks(string role, string source, EstateDrawLayer layer, bool expected)
        {
            var item = new EstateDrawItem { Role = role, Source = source, Layer = layer, Colour = Color.white };
            Assert.That(EstateTerrainImage.IsDarkBank(item), Is.EqualTo(expected));
            Assert.That(item.Colour, Is.EqualTo(Color.white), "Do not apply a second restoration tint to authored dark art.");
        }

        [TestCase(false)] [TestCase(true)]
        public void BankDisplayFlagUsesOnlyTheSpareUvComponent(bool darkBank)
        {
            var owner = new GameObject("bank-display", typeof(RectTransform), typeof(Canvas));
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(.5f, .5f));
            try
            {
                var canvas = owner.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
                var rect = UiShell.Rect("bank", owner.transform); rect.sizeDelta = new Vector2(100, 100);
                var image = rect.gameObject.AddComponent<EstateTerrainImage>();
                image.sprite = sprite; image.PaintedWidth = 100; image.DarkBank = darkBank;
                Canvas.ForceUpdateCanvases();
                var mesh = image.canvasRenderer.GetMesh(); var uv = new List<Vector4>(); mesh.GetUVs(1, uv);
                Assert.That(uv.Count, Is.EqualTo(4));
                Assert.That(uv.All(v => v.w == (darkBank ? 1 : 0)), Is.True);
                Assert.That(uv.All(v => v.z == 0), Is.True, "The bank must not receive landmark darkening or river alpha masking.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture); }
        }

        [Test] public void MovingResidentCrossesOnlyTheStandingDepthOrderWithoutRebuildingStaticObjects()
        {
            var host = new GameObject("estate-depth-fixture", typeof(RectTransform));
            try
            {
                RectTransform Child(string name)
                {
                    var child = new GameObject(name, typeof(RectTransform)); child.transform.SetParent(host.transform, false);
                    return (RectTransform)child.transform;
                }
                var back = Child("back"); var front = Child("front"); var resident = Child("carrier");
                var overlay = Child("front-rail");
                var model = new EstateMapModel(20, 20); model.SetFrame(new Vector2(390, 844), 0, 0);
                var points = new[] { Vector2.zero, new Vector2(0, 4) };
                var route = new EstateCarryRoute(points);
                var carry = resident.gameObject.AddComponent<EstateCarryView>();
                carry.Configure(EstateArt.Load(), model, points, new List<(RectTransform Rect, float Y)> { (back, 16), (front, 48) });
                carry.RefreshAt(0);
                Assert.That(resident.GetSiblingIndex(), Is.LessThan(back.GetSiblingIndex()));
                carry.RefreshAt(route.Length / EstateCarryRoute.PixelsPerSecond * .5);
                Assert.That(resident.GetSiblingIndex(), Is.GreaterThan(back.GetSiblingIndex()));
                Assert.That(resident.GetSiblingIndex(), Is.LessThan(front.GetSiblingIndex()));
                carry.RefreshAt(route.Length / EstateCarryRoute.PixelsPerSecond);
                Assert.That(resident.GetSiblingIndex(), Is.GreaterThan(front.GetSiblingIndex()));
                Assert.That(resident.GetSiblingIndex(), Is.LessThan(overlay.GetSiblingIndex()));
                Assert.That(host.transform.childCount, Is.EqualTo(4));
                var sprite = resident.GetComponent<Image>().sprite;
                Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(256, 256)));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}
