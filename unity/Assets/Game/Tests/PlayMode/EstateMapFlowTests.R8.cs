#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed partial class EstateMapFlowTests
    {
        [UnityTest, Timeout(180000)]
        public IEnumerator HistoricalR7SaveKeepsItsBytesAndGridThroughTheRealEstateEntry() => HistoricalSave("m3a-prototype-r7", 12);

        [UnityTest, Timeout(180000)]
        public IEnumerator R8HalfRestoredMapUsesNormalProductionAndUnlockCommands() => CaptureR8Progress(7, "half-7-of-13");

        [UnityTest, Timeout(180000)]
        public IEnumerator R8AllThirteenZonesOpenThroughNormalProductionAndUnlockCommands() => CaptureR8Progress(13, "full-13-of-13");

        [Serializable] sealed class R8ReviewRiver { public R8ReviewRiverChunk[] chunks; }
        [Serializable] sealed class R8ReviewRiverChunk { public string lit, dark; public float x, y, width, height; }

        IEnumerator CaptureR8Progress(int target, string label)
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var config = CanonicalContent.CreateAccountEstateConfig();
            Assert.That(config.Version, Is.EqualTo(EstateR8Layout.Version));
            long clock = 0; int claims = 0;
            var state = AccountEstate.NewPrototype(catalog, config, 0, 0);
            void Advance(long seconds)
            {
                clock += seconds;
                var advanced = AccountEstate.Advance(catalog, config, state, clock, clock);
                Assert.That(advanced.Clock, Is.EqualTo(AccountEstateClock.Accepted)); state = advanced.State;
            }
            foreach (var kind in new[] { "meta:granary", "m3a:field", "m3a:lumber_camp", "meta:forge", "m3a:ironworks" })
            {
                var building = state.Estate.Buildings.Single(b => b.Kind == kind);
                var build = AccountEstate.StartConstruction(catalog, config, state, clock, clock, building.Id);
                Assert.That(build.Done, Is.True, kind + ": " + build.Reason); state = build.State;
                Advance(state.Estate.Active.RemainingSeconds);
            }
            bool Adjacent(AccountEstateZone a, AccountEstateZone b) =>
                (a.X + a.Width == b.X || b.X + b.Width == a.X) && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height ||
                (a.Y + a.Height == b.Y || b.Y + b.Height == a.Y) && a.X < b.X + b.Width && b.X < a.X + a.Width;
            while (state.Estate.OpenZones.Length < target)
            {
                var zone = config.Zones.First(z => !state.Estate.OpenZones.Contains(z.Id) && config.Zones.Any(o => state.Estate.OpenZones.Contains(o.Id) && Adjacent(o, z)));
                while (state.Wallet["meta:timber"] < zone.Unlock.Cost["meta:timber"])
                {
                    Assert.That(clock, Is.LessThan(86400)); Advance(60);
                    var claim = AccountEstate.ClaimProduction(catalog, config, state, clock, clock);
                    Assert.That(claim.Done, Is.True, claim.Reason); state = claim.State; claims++;
                }
                var unlock = AccountEstate.StartZoneUnlock(catalog, config, state, clock, clock, zone.Id);
                Assert.That(unlock.Done, Is.True, zone.Id + ": " + unlock.Reason); state = unlock.State;
                Advance(zone.Unlock.Seconds);
            }
            Assert.That(claims, Is.GreaterThan(1)); Assert.That(state.Estate.Buildings.Length, Is.EqualTo(10));
            SaveFixture(catalog, state);
            PlayerPrefs.SetInt(prefix + "estatePrototype", 1); PlayerPrefs.Save();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("EstateMap");
            Assert.That(app.EstateMap.State.Estate.OpenZones.Length, Is.EqualTo(target));
            Assert.That(app.EstateMap.FromSave, Is.True);
            Click("EstateMap.Overview"); Canvas.ForceUpdateCanvases();
            Assert.That(app.EstateMap.Map.Zoom, Is.EqualTo(app.EstateMap.Map.WholeMap));
            var map = app.EstateMap.Map;
            Assert.That(map.MapSize.x, Is.LessThanOrEqualTo(map.Viewport.x + .01f));
            Assert.That(map.MapSize.y, Is.LessThanOrEqualTo(map.Viewport.y - map.TopInset - map.BottomInset + .01f));
            Assert.That(map.MapSize.x / map.MapSize.y, Is.EqualTo(map.MapExtent.x / map.MapExtent.y).Within(.001f), "Compact r8 keeps the approved projection, not the historical tall-map framing.");
            var river = JsonUtility.FromJson<R8ReviewRiver>(Resources.Load<TextAsset>("EstateZoneRiver").text);
            Assert.That(river.chunks, Is.Not.Empty);
            var items = app.EstateMap.Plan(EstateDecorRules.Load(), EstateArt.Load()).Items;
            Assert.That(items.Any(i => i.Source == "river-bank" || i.Role == "riverbank-patch" || i.Role == "river-bend-patch"), Is.False,
                "Registered r2 water chunks replace every legacy repeated strip and bank.");
            var actualRiver = items.Where(i => i.Source == "zone-river").ToArray();
            Assert.That(actualRiver.Length, Is.EqualTo(river.chunks.Length));
            Assert.That(actualRiver.Select(i => i.Key).Distinct().Count(), Is.EqualTo(river.chunks.Length));
            foreach (var chunk in river.chunks)
            {
                var drawn = actualRiver.Single(i => i.Key == chunk.lit);
                var centre = EstateMapModel.Unproject(new Vector2(chunk.x + chunk.width / 2, chunk.y + chunk.height / 2));
                var lit = config.Zones.Any(z => state.Estate.OpenZones.Contains(z.Id) && centre.x >= z.X && centre.x < z.X + z.Width && centre.y >= z.Y && centre.y < z.Y + z.Height);
                Assert.That(drawn.Role, Is.EqualTo(lit ? chunk.lit : chunk.dark));
                Assert.That(drawn.Grid, Is.EqualTo(EstateMapModel.Unproject(new Vector2(chunk.x, chunk.y))));
                Assert.That(drawn.Width, Is.EqualTo(chunk.width));
                Assert.That(drawn.Layer, Is.EqualTo(EstateDrawLayer.Terrain));
            }
            CheckBatchBudget(config.Version, "on");
            var openedArea = config.Zones.Where(z => state.Estate.OpenZones.Contains(z.Id)).Sum(z => z.Width * z.Height);
            Debug.Log($"r8 {label} area={openedArea}/1664: normal Core commands with simulated clock, elapsed={clock}s claims={claims}; 10 authored sites, not the concept's future developed building count.");
            yield return Capture("estate-r8-" + label);
            Assert.That(map.UsesBodyCamera, Is.True);
            var settings = EstateMapModel.CameraSettings.Load();
            Assert.That(settings.defaultBuildingWidthPx, Is.EqualTo(75));
            Assert.That(settings.minimumBuildingWidthPx, Is.EqualTo(50));
            foreach (var scene in new[] {
                (Name: "start-village", Centre: new Vector2(28, 29)),
                (Name: "river-fallow-riverside", Centre: new Vector2(45, 34)),
                (Name: "forest-hill", Centre: new Vector2(21, 16)) })
            {
                foreach (var zoom in new[] { map.Opening, 1 })
                {
                    ZoomTo(zoom); Focus(scene.Centre);
                    var width = zoom == map.Opening ? settings.defaultBuildingWidthPx : settings.minimumBuildingWidthPx;
                    var capture = $"estate-r8-{label}-{scene.Name}-{width}px";
                    var actualCentre = map.ViewportToGrid(map.VisibleCentre);
                    Debug.Log($"{capture}: requestedCentre={scene.Centre} actualCentre={actualCentre} clamped={Vector2.Distance(scene.Centre, actualCentre) > .01f} scale={map.Scale} viewport={map.Viewport} topInset={map.TopInset} bottomInset={map.BottomInset}; actual body-width camera, not Astra's offline scale; normal-command fixture has five completed buildings.");
                    CheckBatchBudget(config.Version, "on");
                    var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../artifacts/portrait-234/editor-meta"));
                    foreach (var prefix in new[] { "batch", "batch-runs" })
                    {
                        System.IO.File.Copy(System.IO.Path.Combine(output, $"{prefix}-{config.Version}-on.csv"),
                            System.IO.Path.Combine(output, $"{prefix}-{capture}.csv"), true);
                    }
                    var counts = new int[3];
                    yield return Batches(counts);
                    Debug.Log($"{capture}: batches={counts[0]} drawCalls={counts[1]} setPass={counts[2]}; alpha occupancy and post-occlusion clipped prop counts NOT_MEASURED.");
                    yield return Capture(capture);
                }
            }
        }
    }
}
#endif
