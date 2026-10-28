#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    // M3a-2 estate map prototype (#366) behind its development option, on the real portrait app.
    public sealed partial class EstateMapFlowTests
    {
        const string Toggle = "Settings.영지 지도 시제품 (개발)";
        string directory, prefix, priorDirectory, priorPrefix;
        int priorWidth, priorHeight;
        RunCoordinator app;
        bool? priorAsyncCompilation;
        static string ShaderProbeMode => Environment.GetEnvironmentVariable("M3A7_SHADER_PROBE") ?? "baseline";

        [UnitySetUp] public IEnumerator Setup()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            Assert.That(Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None), Is.Empty);
            priorAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            if (ShaderProbeMode == "off") ShaderUtil.allowAsyncCompilation = false;
            priorDirectory = RunCoordinator.MetaDirectoryOverride; priorPrefix = RunCoordinator.PreferencePrefixOverride;
            priorWidth = Screen.width; priorHeight = Screen.height;
            var id = Guid.NewGuid().ToString("N");
            directory = Path.Combine(Path.GetTempPath(), "sowsiege-estate-map-" + id);
            prefix = "sowsiege.test.estate-map." + id + ".";
            RunCoordinator.MetaDirectoryOverride = directory; RunCoordinator.PreferencePrefixOverride = prefix;
            // Skip the first-run introduction so the title opens the estate tab directly.
            PlayerPrefs.SetInt(prefix + "introductionSeen", 1); PlayerPrefs.Save();
            PlayModeWindow.SetCustomRenderingResolution(390, 844, "Estate map QA");
            yield return null;
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (priorAsyncCompilation.HasValue) { ShaderUtil.allowAsyncCompilation = priorAsyncCompilation.Value; priorAsyncCompilation = null; }
            foreach (var boot in Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None)) Object.Destroy(boot.gameObject);
            yield return null;
            if (directory != null)
            {
                RunCoordinator.MetaDirectoryOverride = priorDirectory; RunCoordinator.PreferencePrefixOverride = priorPrefix;
                foreach (var key in RunPreferences.Keys) PlayerPrefs.DeleteKey(prefix + key);
                PlayerPrefs.Save();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                PlayModeWindow.SetCustomRenderingResolution((uint)priorWidth, (uint)priorHeight, "Restored test viewport");
            }
            app = null;
            yield return null;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator DevelopmentOptionSwitchesTheEstateTabToTheIsometricMapAndBack()
        {
            var historicalCatalog = CanonicalContent.CreateMetaCatalog();
            var historicalConfig = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            SaveFixture(historicalCatalog, AccountEstate.NewPrototype(historicalCatalog, historicalConfig, 0, 0));
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            Assert.That(Buttons("Estate.Priority:").Length, Is.EqualTo(4), "The option is off by default: the manor page is unchanged.");
            var account = MetaSaveCodec.Encode(app.Progression.State);

            yield return SetOption(true); yield return ScreenIs("EstateMap");
            var screen = app.EstateMap;
            Assert.That(Rects("EstateMap.Building:").Length, Is.EqualTo(screen.State.Estate.Buildings.Count(b => b.Level > 0 || screen.UnderConstruction(b.Id))));
            foreach (var site in screen.State.Estate.Buildings.Where(b => b.Level == 0 && !screen.UnderConstruction(b.Id)))
                Assert.That(Rects("EstateMap.Site:" + site.Id + ":").Length, Is.EqualTo(1), "An unbuilt site uses one approved Astra footprint instead of temporary separate fragments.");
            Assert.That(Buttons("EstateMap.Building:"), Is.Empty, "Buildings carry no rectangular buttons; taps use the diamond grid.");
            Assert.That(Rects("EstateMap.NameTag"), Is.Empty, "Names are hidden until a building is selected.");
            Assert.That(Buttons("Estate.Priority:"), Is.Empty);
            var expected = screen.State.Estate.Buildings.Where(b => b.Level > 0 || screen.UnderConstruction(b.Id)).OrderBy(b => EstateMapModel.SortY(b.X, b.Y, 2, 2)).ThenBy(b => b.Id).Select(b => "EstateMap.Building:" + b.Id).ToArray();
            Assert.That(Rects("EstateMap.Building:").OrderBy(r => r.GetSiblingIndex()).Select(r => r.name).ToArray(), Is.EqualTo(expected), "Draw order is front corner y, then instance order.");
            // #379: the first visit frames the lit land with a ring of night (key art C); the map carries no zone labels and one
            // continuous night; the marker for the next zone shows only in the whole view.
            var map = screen.Map;
            Assert.That(map.Zoom, Is.EqualTo(map.Opening));
            Assert.That(Rects("EstateMap.Night").Length, Is.EqualTo(1)); Assert.That(Rects("EstateMap.Fog"), Is.Empty);
            Assert.That(app.Ui.GetComponentsInChildren<Text>().Any(t => t.text.Contains("탐사로 열 땅")), Is.False, "No zone labels on the map.");
            Assert.That(Named("EstateMap.NextZone", true).gameObject.activeSelf, Is.False);
            var lit = screen.Config.Zones.Single(z => z.OpenAtStart);
            Assert.That(map.GridToViewport(new Vector2(lit.X, lit.Y + lit.Height)).x, Is.GreaterThanOrEqualTo(-.01f), "The complete opening remains inside the viewport.");
            foreach (var corner in new[] { new Vector2(lit.X, lit.Y), new Vector2(lit.X + lit.Width, lit.Y),
                new Vector2(lit.X, lit.Y + lit.Height), new Vector2(lit.X + lit.Width, lit.Y + lit.Height) })
            {
                var point = map.GridToViewport(corner);
                Assert.That(point.x, Is.InRange(-.01f, map.Viewport.x + .01f));
                Assert.That(point.y, Is.InRange(map.TopInset - .01f, map.Viewport.y - map.BottomInset + .01f));
            }
            var lightDiameter = Mathf.Max(lit.Width, lit.Height) * Mathf.Sqrt(2) * EstateMapModel.HalfWidth;
            Assert.That(lightDiameter * map.Scale / map.Viewport.x, Is.EqualTo(Mathf.Sqrt(.5f)).Within(.02f), "r7 fits its entire 12x12 projected opening while keeping rounded light (#389).");
            yield return Capture("estate-map-first");
            yield return NightComparison("estate-map-first");
            Assert.That(PlayerPrefs.GetString(prefix + "estateMapView", ""), Is.Empty, "Nothing is remembered until the player moves the map.");

            ZoomTo(EstateMapModel.Neighbourhood);
            var step = map.GridToViewport(Vector2.right) - map.GridToViewport(Vector2.zero);
            Assert.That(Vector2.Distance(step, new Vector2(32, 16)), Is.LessThan(1e-3f), "Neighbourhood cells are 64×32 diamonds.");
            Assert.That(PlayerPrefs.GetString(prefix + "estateMapView", ""), Does.StartWith(EstateMapModel.Neighbourhood + ";"), "A zoom change is remembered.");
            yield return Capture("estate-map-neighbourhood");
            yield return MeasureDecor(screen);

            ZoomTo(map.WholeMap);
            // The vertical estate (m3a-prototype-r4) uses most of the portrait screen's height in the whole view (#375).
            Assert.That(map.MapSize.y, Is.GreaterThanOrEqualTo(.7f * map.Viewport.y), "The whole estate fills at least 70% of the height.");
            Assert.That(map.MapSize.x, Is.LessThanOrEqualTo(map.Viewport.x + 1e-2f));
            Assert.That(Named("EstateMap.NextZone", true).gameObject.activeSelf, Is.True, "The whole view marks the zone that opens next.");
            yield return Capture("estate-map-whole");
            yield return NightComparison("estate-map-whole");
            ZoomTo(EstateMapModel.BuildingDetail);
            yield return Capture("estate-map-building");
            Click("EstateMap.ZoomIn"); yield return null; Assert.That(map.Zoom, Is.EqualTo(EstateMapModel.BuildingDetail), "Four levels only.");
            Click("EstateMap.ZoomOut"); yield return null; Assert.That(map.Zoom, Is.EqualTo(EstateMapModel.Neighbourhood));

            var viewport = (RectTransform)View().transform;
            var offset = screen.Map.Offset;
            Drag(viewport, Pixels(viewport).center, Pixels(viewport).center + new Vector2(-120, 0) * app.Ui.Canvas.scaleFactor);
            yield return null;
            Assert.That(screen.Map.Offset.x, Is.GreaterThan(offset.x + 100), "A one-finger drag left shows more of the east.");
            var east = screen.Map.Offset.x;
            Drag(viewport, Pixels(viewport).center, Pixels(viewport).center + new Vector2(200, 0) * app.Ui.Canvas.scaleFactor);
            yield return null;
            Assert.That(screen.Map.Offset.x, Is.LessThan(east - 150), "A drag right shows more of the west.");
            ZoomTo(map.WholeMap);
            Drag(viewport, Pixels(viewport).center, Pixels(viewport).center + new Vector2(4000, 0) * app.Ui.Canvas.scaleFactor);
            yield return null;
            Assert.That(screen.Map.GridToViewport(new Vector2(0, 20)).x, Is.GreaterThanOrEqualTo(-1e-2f), "The whole map fits, so no drag reveals space past the west hill's corner.");
            ZoomTo(EstateMapModel.Neighbourhood);

            Click("EstateMap.Goal"); yield return null; Assert.That(screen.GoalCollapsed, Is.True);
            yield return Capture("estate-map-goal-folded");
            Click("EstateMap.Goal"); yield return null; Assert.That(screen.GoalCollapsed, Is.False);

            var forge = screen.State.Estate.Buildings.Single(b => b.Kind == "meta:forge");
            Focus(screen.Centre(forge.Id)); Tap(new Vector2Int(forge.X + 1, forge.Y + 1)); yield return null;
            Assert.That(screen.Selected, Is.EqualTo(forge.Id), "A tap on any cell of the footprint selects the building.");
            Assert.That(Rects("EstateMap.NameTag"), Is.Empty, "Names stay in the sheet instead of hiding building art.");
            Assert.That(Vector2.Distance(screen.Map.GridToViewport(screen.Centre(forge.Id)), screen.Map.VisibleCentre), Is.LessThan(1), "Selection centres its foundation above the sheet.");
            Assert.That(Buttons("EstateMap.Zoom"), Is.Empty); Assert.That(Buttons("EstateMap.Decor"), Is.Empty);
            Assert.That(Named("EstateMap.Move").interactable, Is.True);
            Assert.That(screen.Map.InView(screen.Centre(forge.Id)), Is.True, "The selected building stays between the bars and the panel.");
            Assert.That(screen.Map.MapSize.y, Is.GreaterThan(screen.Map.Viewport.y - screen.Map.TopInset - screen.Map.BottomInset), "The wide map can be dragged to every row behind the panel.");
            yield return Capture("estate-map-selected");
            Click("EstateMap.Move"); yield return null; Assert.That(screen.Moving, Is.True);
            // At building zoom the original footprint leaves the screen once the player pans to the destination; the redraw
            // after the tap must keep the destination preview in view (review on #369).
            View().Step(1); yield return null; Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.BuildingDetail));
            var destination = VacantCell(screen, 2, 2, screen.Centre(forge.Id));
            Focus(destination + new Vector2(.5f, .5f)); Tap(destination); yield return null;
            Assert.That(screen.Preview, Is.EqualTo(destination)); Assert.That(screen.PreviewValid, Is.True, screen.PreviewReason);
            Assert.That(screen.Map.InView(destination + Vector2.one), Is.True, "The destination preview stays on screen after the tap redraws the page.");
            Assert.That(screen.Map.InView(screen.Centre(forge.Id)), Is.False, "The camera did not jump back to the original footprint.");
            View().Step(-1); yield return null;
            yield return Capture("estate-map-move-valid");
            Click("EstateMap.Confirm"); yield return null;
            Assert.That(screen.Moving, Is.False);
            Assert.That(screen.Building(forge.Id).X, Is.EqualTo(destination.x)); Assert.That(screen.Building(forge.Id).Y, Is.EqualTo(destination.y));

            var home = screen.State.Estate.Buildings.Single(b => b.Kind == "m3a:home");
            Click("EstateMap.Move"); yield return null;
            Focus(screen.Centre(home.Id)); Tap(new Vector2Int(home.X, home.Y)); yield return null;
            Assert.That(screen.PreviewValid, Is.False); Assert.That(screen.PreviewReason, Is.EqualTo("overlaps:" + home.Id));
            Assert.That(Named("EstateMap.Confirm").interactable, Is.False);
            yield return Capture("estate-map-move-blocked");
            Click("EstateMap.Cancel"); yield return null;
            Assert.That(screen.Building(forge.Id).X, Is.EqualTo(destination.x), "Cancel keeps the last confirmed place.");
            var empty = VacantCell(screen, 1, 1, screen.Centre(forge.Id));
            Focus(empty + new Vector2(.5f, .5f)); Tap(empty); yield return null;
            Assert.That(screen.Selected, Is.Null, "A tap on empty open ground closes the selection.");

            Assert.That(app.Progression.State.Estate, Is.Null);
            Assert.That(MetaSaveCodec.Encode(app.Progression.State), Is.EqualTo(account), "The prototype only reads the account.");
            yield return SetOption(false); yield return ScreenIs("Estate");
            Assert.That(Buttons("Estate.Priority:").Length, Is.EqualTo(4));
            Assert.That(measuredBreaks.On, Is.LessThanOrEqualTo(17 + 4), "Base on17 plus at most four landmark batches.");
            Assert.That(measuredBreaks.Off, Is.LessThanOrEqualTo(9 + 4), "Base off9 plus at most four landmark batches.");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator FreshR8EstateBuildsItsGranaryBeforeTheFieldAndCompletesWithoutAccountWrites()
        {
            PlayerPrefs.SetInt(prefix + "estatePrototype", 1); PlayerPrefs.Save();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("EstateMap");
            var screen = app.EstateMap;
            var account = MetaSaveCodec.Encode(app.Progression.State);
            Assert.That(screen.Config.Version, Is.EqualTo("m3a-prototype-r8"));
            Assert.That(screen.FromSave, Is.False);
            Assert.That(screen.State.Wallet["meta:grain"], Is.EqualTo(120));
            Assert.That(screen.State.Wallet["meta:timber"], Is.EqualTo(180));
            yield return Capture("estate-r8-first-entry");
            ZoomTo(screen.Map.WholeMap);
            yield return Capture("estate-r8-whole");
            ZoomTo(screen.Map.Opening);

            var field = screen.State.Estate.Buildings.Single(b => b.Kind == "m3a:field");
            Focus(screen.Centre(field.Id)); Tap(new Vector2Int(field.X, field.Y)); yield return null;
            Assert.That(Named("EstateMap.Build").interactable, Is.False);
            Assert.That(app.Ui.GetComponentsInChildren<Text>().Single(t => t.name == "EstateMap.ConstructionReason").text, Is.EqualTo("곡물창고 Lv1 필요"));
            Assert.That(screen.State.Estate.Active, Is.Null, "A fresh field still requires the granary; do not grant resources or bypass Core.");
            Assert.That(screen.ConstructionPreview(field.Id).Reason, Is.EqualTo("parent-required"));
            yield return Capture("estate-r8-field-parent-required");
            var granary = screen.State.Estate.Buildings.Single(b => b.Kind == "meta:granary");
            Focus(screen.Centre(granary.Id)); Tap(new Vector2Int(granary.X, granary.Y)); yield return null;
            Click("EstateMap.Build"); yield return null;
            Assert.That(screen.LastConstructionReason, Is.EqualTo("started"));
            Assert.That(screen.State.Estate.Active.BuildingId, Is.EqualTo(granary.Id));
            yield return CaptureNaturalConstruction(granary.Id, false, "estate-r8-granary");
            Assert.That(screen.Building(granary.Id).Level, Is.EqualTo(1));

            Focus(screen.Centre(field.Id)); Tap(new Vector2Int(field.X, field.Y)); yield return null;
            Click("EstateMap.Build"); yield return null;
            Assert.That(screen.LastConstructionReason, Is.EqualTo("started"));
            Assert.That(screen.State.Estate.Active.BuildingId, Is.EqualTo(field.Id));
            Assert.That(Rects("EstateMap.ConstructionSign"), Is.Empty, "The selected building sheet names construction without covering its artwork.");
            var fieldImage = app.Ui.GetComponentsInChildren<Image>().Single(i => i.name == "EstateMap.Building:" + field.Id);
            Assert.That(fieldImage.sprite, Is.SameAs(EstateArt.Load().Get("construction-field-1-lit")));
            Click("EstateMap.Close"); yield return null;
            Canvas.ForceUpdateCanvases(); View().Layout();
            var sign = Rects("EstateMap.ConstructionSign").Single();
            var viewport = Named("EstateMap.Viewport", false);
            var fieldKind = screen.Kind(field.Kind);
            var front = screen.Map.GridToViewport(new Vector2(field.X + fieldKind.Width, field.Y + fieldKind.Height));
            var worldFront = viewport.TransformPoint(new Vector3(viewport.rect.xMin + front.x, viewport.rect.yMax - front.y));
            var signCorners = new Vector3[4]; sign.GetWorldCorners(signCorners);
            Assert.That(signCorners[1].y, Is.LessThanOrEqualTo(worldFront.y), "Closed-sheet label stays below the foundation front edge.");
            Tap(new Vector2Int(field.X, field.Y)); yield return null;
            Assert.That(Rects("EstateMap.ConstructionSign"), Is.Empty, "Reopening the sheet removes the redundant map label.");
            yield return CaptureNaturalConstruction(field.Id, true, "estate-r8-field");
            Assert.That(screen.Building(field.Id).Level, Is.EqualTo(1));
            Assert.That(screen.State.Estate.Active, Is.Null);
            Assert.That(Rects("EstateMap.ConstructionSign"), Is.Empty);
            yield return Capture("estate-r8-field-complete");
            Assert.That(MetaSaveCodec.Encode(app.Progression.State), Is.EqualTo(account), "Only the prototype copy changes.");
        }

        // Actual Editor development-copy UI: normal Build commands and the view's real monotonic clock only.
        // No native claim, synthetic sprite substitution, resource grant or development time skip.
        IEnumerator CaptureNaturalConstruction(int buildingId, bool field, string name)
        {
            var screen = app.EstateMap;
            Assert.That(screen.State.Estate.Active?.BuildingId, Is.EqualTo(buildingId));
            var deadline = Time.realtimeSinceStartupAsDouble + screen.State.Estate.Active.TotalSeconds + 15;
            for (var stage = 1; stage <= 3; stage++)
            {
                while (EstateConstructionArt.Stage(screen.State.Estate.Active) < stage)
                {
                    Assert.That(screen.State.Estate.Active?.BuildingId, Is.EqualTo(buildingId), "Work ended before its stage capture.");
                    Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Normal construction clock timed out.");
                    yield return null;
                }
                // The normal view requests its redraw when the stage changes; let that redraw settle.
                yield return null;
                Assert.That(screen.State.Estate.Active?.BuildingId, Is.EqualTo(buildingId));
                Assert.That(EstateConstructionArt.Stage(screen.State.Estate.Active), Is.EqualTo(stage), "Do not label a skipped stage as observed.");
                var role = EstateConstructionArt.Role(field, stage, true);
                var sprite = app.Ui.GetComponentsInChildren<Image>().Single(i => i.name == "EstateMap.Building:" + buildingId).sprite;
                Assert.That(sprite, Is.SameAs(EstateArt.Load().Get(role)), "Capture the actual stage sprite, not only the clock state.");
                var captureName = stage == 1 ? name + "-construction" : name + "-construction-stage-" + stage;
                yield return Capture(captureName);
                Assert.That(EstateConstructionArt.Stage(screen.State.Estate.Active), Is.EqualTo(stage), "Capture crossed a stage boundary.");
                var work = screen.State.Estate.Active;
                var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-234/editor-meta"));
                var csv = Path.Combine(output, "estate-r7-construction-observations.csv");
                if (!File.Exists(csv)) File.WriteAllText(csv, "name,source,surface,buildingId,stage,totalSeconds,remainingSeconds,role,frame\n");
                File.AppendAllText(csv, $"{captureName},{BuildIdentity.Commit},Editor-development-copy-normal-clock,{buildingId},{stage},{work.TotalSeconds},{work.RemainingSeconds},{role},{Time.frameCount}\n");
            }
            while (screen.State.Estate.Active != null)
            {
                Assert.That(screen.State.Estate.Active.BuildingId, Is.EqualTo(buildingId));
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Normal construction completion timed out.");
                yield return null;
            }
            yield return null;
            Assert.That(screen.Building(buildingId).Level, Is.EqualTo(1));
            Assert.That(Rects("EstateMap.ConstructionSign"), Is.Empty);
            if (!field) yield return Capture(name + "-complete");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator CompletedIronworksUsesApprovedArtInAnExplicitEditorFixture()
        {
            var catalog = CanonicalContent.CreateMetaCatalog();
            var config = CanonicalContent.CreateAccountEstateConfig("m3a-prototype-r7");
            var fixture = AccountEstate.Migrate(catalog, config, MetaEngine.NewGame(catalog), 0, 0);
            fixture = fixture with
            {
                Estate = fixture.Estate with
                {
                    Buildings = fixture.Estate.Buildings.Select(b =>
                        b.Kind == "meta:forge" || b.Kind == "m3a:ironworks" ? b with { Level = 1 } : b).ToArray()
                }
            };
            SaveFixture(catalog, fixture);
            PlayerPrefs.SetInt(prefix + "estatePrototype", 1); PlayerPrefs.Save();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("EstateMap");
            var screen = app.EstateMap;
            var account = MetaSaveCodec.Encode(app.Progression.State);
            var disk = File.ReadAllBytes(Path.Combine(directory, "progress.bin"));
            Assert.That(screen.FromSave, Is.True);
            var ironworks = screen.State.Estate.Buildings.Single(b => b.Kind == "m3a:ironworks");
            Assert.That(ironworks.Level, Is.EqualTo(1));
            Assert.That(screen.State.Estate.Active, Is.Null);
            Focus(screen.Centre(ironworks.Id)); Tap(new Vector2Int(ironworks.X, ironworks.Y)); yield return null;
            var art = app.Ui.GetComponentsInChildren<Image>().Single(i => i.name == "EstateMap.Building:" + ironworks.Id);
            Assert.That(art.sprite, Is.SameAs(EstateArt.Load().Body("ironworks", 1)));
            Assert.That(art.sprite, Is.Not.SameAs(EstateArt.Load().Body("forge", 1)), "Ironworks cannot reuse forge completion art.");
            Assert.That(Rects("EstateMap.ConstructionSign"), Is.Empty);
            const string name = "estate-r7-ironworks-complete-editor-fixture";
            const string provenance = "fixture=Editor saved-state completed forge+ironworks; not native normal-construction evidence; no resource grants or native state mutation";
            Debug.Log(provenance);
            yield return Capture(name);
            var note = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-234/editor-meta",
                name + "-" + Screen.width + "x" + Screen.height + ".png.txt"));
            File.AppendAllText(note, provenance + "\n");
            Assert.That(MetaSaveCodec.Encode(app.Progression.State), Is.EqualTo(account));
            Assert.That(File.ReadAllBytes(Path.Combine(directory, "progress.bin")), Is.EqualTo(disk));
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator ASavedEstateBuildsClaimsAndSpecializesOnTheMap()
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var config = CanonicalContent.CreateAccountEstateConfig();
            var old = MetaEngine.NewGame(catalog);
            old = old with
            {
                ManorLevels = new Dictionary<string, int>(old.ManorLevels) { ["meta:granary"] = 3, ["meta:forge"] = 1 },
                Wallet = new Dictionary<string, int>(old.Wallet) { ["meta:grain"] = 300, ["meta:timber"] = 400, ["meta:iron"] = 150 },
            };
            var fixture = AccountEstate.Migrate(catalog, config, old, 0, 0);
            SaveFixture(catalog, fixture);
            PlayerPrefs.SetInt(prefix + "estatePrototype", 1); PlayerPrefs.Save();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("EstateMap");
            var screen = app.EstateMap; var account = MetaSaveCodec.Encode(app.Progression.State);
            Assert.That(screen.FromSave, Is.True, "An account that carries an estate is copied, not replaced by a new one.");
            int Id(string kind) => screen.State.Estate.Buildings.Single(b => b.Kind == kind).Id;

            var field = screen.Building(Id("m3a:field"));
            Focus(screen.Centre(field.Id)); Tap(new Vector2Int(field.X, field.Y)); yield return null;
            Assert.That(Named("EstateMap.Build").GetComponentInChildren<Text>().text, Is.EqualTo("짓기"));
            Click("EstateMap.Build"); yield return null;
            Assert.That(screen.State.Estate.Active.BuildingId, Is.EqualTo(field.Id)); Assert.That(screen.Notice, Is.EqualTo("경작지 공사 시작"));
            Assert.That(Rects("EstateMap.ConstructionSign"), Is.Empty, "The selected building sheet names construction without covering its artwork.");
            var fieldArt = app.Ui.GetComponentsInChildren<Image>().Single(i => i.name == "EstateMap.Building:" + field.Id);
            Assert.That(fieldArt.sprite, Is.SameAs(EstateArt.Load().Get("construction-field-1-lit")), "Construction uses the approved field stage, with its own authored ground anchor.");
            Assert.That(fieldArt.color, Is.EqualTo(Color.white));
            Assert.That(Vector2.Distance(screen.Map.GridToViewport(screen.Centre(field.Id)), screen.Map.VisibleCentre), Is.LessThan(1));
            yield return Capture("estate-flow-build");
            Click("EstateMap.Skip60"); yield return null;
            Assert.That(screen.Building(field.Id).Level, Is.EqualTo(1));

            var forge = screen.Building(Id("meta:forge"));
            Focus(screen.Centre(forge.Id)); Tap(new Vector2Int(forge.X, forge.Y)); yield return null;
            Assert.That(Named("EstateMap.Build").GetComponentInChildren<Text>().text, Is.EqualTo("올리기"));
            Click("EstateMap.Build"); yield return null;
            var lumber = screen.Building(Id("m3a:lumber_camp"));
            Focus(screen.Centre(lumber.Id)); Tap(new Vector2Int(lumber.X, lumber.Y)); yield return null;
            Click("EstateMap.Build"); yield return null; Assert.That(screen.Reserved(lumber.Id), Is.True, "A second order while busy is reserved.");
            Click("EstateMap.QueueToggle"); yield return null; Assert.That(screen.QueueOpen, Is.True);
            Assert.That(Rects("EstateMap.Queue").Count(r => r.name == "EstateMap.Queue"), Is.EqualTo(1));
            yield return Capture("estate-flow-queue");
            Click("EstateMap.CancelReservation:" + lumber.Id); yield return null; Assert.That(screen.Reserved(lumber.Id), Is.False);
            Click("EstateMap.Close"); yield return null;

            Click("EstateMap.Skip600"); yield return null;
            Assert.That(screen.Building(forge.Id).Level, Is.EqualTo(2));
            Assert.That(screen.StockTotal, Is.GreaterThan(0));
            yield return Capture("estate-flow-stock");
            var grain = screen.State.Wallet["meta:grain"]; var stock = screen.StockTotal;
            Click("EstateMap.ClaimStock"); yield return null;
            Assert.That(screen.State.Wallet["meta:grain"], Is.EqualTo(grain + stock)); Assert.That(screen.StockTotal, Is.EqualTo(0));
            Click("EstateMap.GoalClaim"); yield return null;
            Assert.That(screen.State.Estate.ClaimedTasks, Does.Contain("task:m3a-first-claim"));

            var granary = screen.Building(Id("meta:granary"));
            Focus(screen.Centre(granary.Id)); Tap(new Vector2Int(granary.X + 1, granary.Y + 1)); yield return null;
            Click("EstateMap.Specialize"); yield return null; Assert.That(screen.Specializing, Is.True);
            Click("EstateMap.Branch:granary:long-away"); yield return null;
            yield return Capture("estate-flow-specialize");
            Click("EstateMap.BranchConfirm"); yield return null;
            Assert.That(screen.Building(granary.Id).Branch, Is.EqualTo("granary:long-away"));

            Focus(new Vector2(45.5f, 30.5f)); Tap(new Vector2Int(45, 30)); yield return null;
            Assert.That(screen.ZoneChoice, Is.Not.Null); Assert.That(Rects("EstateMap.Zone").Count(r => r.name == "EstateMap.Zone"), Is.EqualTo(1));
            yield return Capture("estate-flow-zone");

            Assert.That(MetaSaveCodec.Encode(app.Progression.State), Is.EqualTo(account), "The prototype never writes the saved estate.");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator HistoricalR3SaveKeepsItsBytesAndGridThroughTheRealEstateEntry() => HistoricalSave("m3a-prototype-r3", 20);

        [UnityTest, Timeout(180000)]
        public IEnumerator HistoricalR4SaveKeepsItsBytesAndGridThroughTheRealEstateEntry() => HistoricalSave("m3a-prototype-r4", 20);

        [UnityTest, Timeout(180000)]
        public IEnumerator HistoricalR5SaveKeepsItsGridThroughTheRealEstateEntry() => HistoricalSave("m3a-prototype-r5", 14);

        [UnityTest, Timeout(180000)]
        public IEnumerator HistoricalR6SaveKeepsItsBytesAndGridThroughTheRealEstateEntry() => HistoricalSave("m3a-prototype-r6", 8);

        IEnumerator HistoricalSave(string version, int openedWidth)
        {
            var catalog = CanonicalContent.CreateMetaCatalog();
            var config = CanonicalContent.CreateAccountEstateConfig(version);
            var fixture = AccountEstate.Migrate(catalog, config, MetaEngine.NewGame(catalog), 0, 0);
            // #386: migration initializes Estate's clock, not the account idle clock. First boot intentionally writes
            // the latter. Prove exact bytes at equal clock input before separating boot from the read-only View gate.
            var initialized = fixture with { LastMonotonicSeconds = 0, LastWallSeconds = 0 };
            SaveFixture(catalog, initialized);
            var fixedDisk = File.ReadAllBytes(Path.Combine(directory, "progress.bin"));
            var fixedHost = new MetaProgression(catalog, CanonicalContent.CreateCatalog(), CanonicalContent.DataHash,
                directory, () => 0, () => 0, MetaProjectionPolicy.AccountOnlyWave, MetaRewardDelivery.PendingClaims);
            Assert.That(MetaSaveCodec.Encode(fixedHost.State), Is.EqualTo(MetaSaveCodec.Encode(initialized)), version + ": equal-clock resume changes no state byte.");
            Assert.That(File.ReadAllBytes(Path.Combine(directory, "progress.bin")), Is.EqualTo(fixedDisk), version + ": equal-clock resume changes no disk byte.");

            SaveFixture(catalog, fixture);
            var bytes = MetaSaveCodec.Encode(fixture);
            PlayerPrefs.SetInt(prefix + "estatePrototype", 1); PlayerPrefs.Save();
            yield return Boot();
            var bootState = app.Progression.State;
            Assert.That(fixture.LastMonotonicSeconds, Is.EqualTo(-1));
            Assert.That(fixture.LastWallSeconds, Is.EqualTo(-1));
            Assert.That(bootState.LastMonotonicSeconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(bootState.LastWallSeconds, Is.GreaterThanOrEqualTo(0));
            // Restore only the two observed clock fields in a diagnostic copy. Any other account or Estate change fails.
            var withoutClockInitialization = bootState with
            {
                LastMonotonicSeconds = fixture.LastMonotonicSeconds,
                LastWallSeconds = fixture.LastWallSeconds
            };
            Assert.That(MetaSaveCodec.Encode(withoutClockInitialization), Is.EqualTo(bytes), version + ": boot must differ only in the two top-level clock fields.");
            var account = MetaSaveCodec.Encode(bootState);
            Assert.That(account.Length, Is.EqualTo(bytes.Length), version + ": boot must not change save layout or length.");
            var changedByteOffsets = Enumerable.Range(0, bytes.Length).Where(i => bytes[i] != account[i])
                .Select(i => $"{i}:{bytes[i]:X2}->{account[i]:X2}");
            var disk = File.ReadAllBytes(Path.Combine(directory, "progress.bin"));
            Debug.Log($"Historical estate save {version}: equal-clock state/disk bytes identical; boot LastMonotonicSeconds={fixture.LastMonotonicSeconds}->{bootState.LastMonotonicSeconds}, LastWallSeconds={fixture.LastWallSeconds}->{bootState.LastWallSeconds}; all other serialized fields identical.");
            Debug.Log($"Historical estate save {version}: actual boot codec byte diff (offset:before->after)=[{string.Join(",", changedByteOffsets)}]; final 32 bytes are the SHA256 envelope checksum; only the two top-level clocks were normalized for the preceding diagnostic equality check.");

            Click("Title.Enter"); yield return ScreenIs("EstateMap");
            Assert.That(app.EstateMap.Config.Version, Is.EqualTo(config.Version));
            Assert.That(app.EstateMap.Config.Zones.Single(z => z.OpenAtStart).Width, Is.EqualTo(openedWidth));
            CheckBatchBudget(version, "on");
            Click("EstateMap.Decor"); yield return null;
            CheckBatchBudget(version, "off");
            Click("EstateMap.Decor"); yield return null;
            AssertHistoricalBytes("first map entry");
            Click("Tab.Vassals"); yield return ScreenIs("Vassals");
            AssertHistoricalBytes("map closed");
            Click("Tab.Estate"); yield return ScreenIs("EstateMap");
            Assert.That(app.EstateMap.Config.Version, Is.EqualTo(version));
            AssertHistoricalBytes("map reopened");
            Debug.Log($"Historical estate save {version}: first entry, close and reopen preserve all {account.Length} account bytes and {disk.Length} disk bytes.");

            void AssertHistoricalBytes(string stage)
            {
                Assert.That(MetaSaveCodec.Encode(app.Progression.State), Is.EqualTo(account), version + ": " + stage + " changed account bytes.");
                Assert.That(File.ReadAllBytes(Path.Combine(directory, "progress.bin")), Is.EqualTo(disk), version + ": " + stage + " changed disk bytes.");
            }
        }

        void SaveFixture(MetaCatalog catalog, MetaState state)
        {
            Assert.That(MetaValidation.ValidateState(catalog, state), Is.Empty);
            new AtomicSaveStore(directory, "progress.bin").Save(MetaSaveCodec.Encode(state), bytes =>
            {
                var decoded = MetaSaveCodec.Decode(bytes);
                return decoded.Valid && MetaValidation.ValidateState(catalog, decoded.State).Length == 0 ? AtomicSaveValidation.Valid : AtomicSaveValidation.Corrupt;
            });
        }

        // #372: decoration matches its plan, uses only catalogued art, and its draw-batch cost is measured with it on and off.
        IEnumerator MeasureDecor(EstateMapScreen screen)
        {
            Assert.That(screen.DecorEnabled, Is.True);
            var plan = screen.Plan(EstateDecorRules.Load(), EstateArt.Load()).Items;
            var decorSources = new[] { "yard", "roadside", "empty-land", "yard-ground", "fence", "peasant", "soil" };
            var sprites = plan.Count(i => decorSources.Contains(i.Source));
            Assert.That(Rects("EstateMap.Decor:").Length, Is.EqualTo(sprites), "Every planned decoration sprite is on the map.");
            Assert.That(Rects("EstateMap.Building:").All(r => r.GetComponent<Image>().sprite != null), Is.True, "Buildings draw Astra's stage art.");
            Assert.That(Rects("EstateMap.Veiled:").Length, Is.EqualTo(screen.Plan(EstateDecorRules.Load(), EstateArt.Load()).In(EstateDrawLayer.Veiled).Count()), "The land to come is painted under the night.");
            var night = Rects("EstateMap.Night").Single(); Assert.That(Rects("EstateMap.Veiled:").All(r => r.GetSiblingIndex() < night.GetSiblingIndex()), Is.True, "The night covers the land to come.");
            Assert.That(Rects("EstateMap.Landmark:").Length, Is.GreaterThan(0));
            Assert.That(Rects("EstateMap.Landmark:").All(r => r.GetSiblingIndex() > night.GetSiblingIndex()), Is.True, "Large silhouettes remain readable above the night.");
            var riverCount = screen.Config.Version == EstateR8Layout.Version
                ? EstateR8Layout.Load().placed.Count(p => p.id.StartsWith("river", StringComparison.Ordinal)) : 7;
            Assert.That(Rects("EstateMap.Landmark:river").Length, Is.EqualTo(riverCount), "Every configured river patch remains readable; historical layouts retain seven.");
            Assert.That(Rects("EstateMap.Lamps").Single().GetSiblingIndex(), Is.LessThan(night.GetSiblingIndex()), "Warm lamps cannot paint brown clouds above the night.");
            Assert.That(Rects("EstateMap.Site").Where(r => r.name == "EstateMap.Site").Concat(Rects("EstateMap.Decor:yard-ground")).All(r => r.GetSiblingIndex() < night.GetSiblingIndex()), Is.True, "Painted yards cannot overwrite the rounded night boundary.");
            Assert.That(Rects("EstateMap.Building:").All(r => r.GetSiblingIndex() > night.GetSiblingIndex()), Is.True, "Buildings in open land stand above the night.");
            Assert.That(Rects("EstateMap.Site:").All(r => r.GetSiblingIndex() < night.GetSiblingIndex()), Is.True, "Approved flat footprints share the painted ground beneath the rounded night boundary.");
            var on = new int[3]; yield return Batches(on); var breaksOn = EstateMapView.MaterialBreaks(View().transform);
            CheckBatchBudget(screen.Config.Version, "on");
            yield return Capture("estate-decor-on");
            Click("EstateMap.Decor"); yield return null; Assert.That(screen.DecorEnabled, Is.False);
            Assert.That(Rects("EstateMap.Decor:"), Is.Empty);
            var off = new int[3]; yield return Batches(off); var breaksOff = EstateMapView.MaterialBreaks(View().transform);
            CheckBatchBudget(screen.Config.Version, "off");
            yield return Capture("estate-decor-off");
            Click("EstateMap.Decor"); yield return null; Assert.That(screen.DecorEnabled, Is.True);
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-234/editor-meta")); Directory.CreateDirectory(output);
            var counts = string.Join(", ", plan.GroupBy(i => i.Source).Select(g => $"\"{g.Key}\": {g.Count()}"));
            File.WriteAllText(Path.Combine(output, "estate-decor-batches.json"),
                $"{{\"commit\": \"{BuildIdentity.Commit}\", \"screen\": \"390x844 neighbourhood\", \"decorSprites\": {sprites}, \"materialBreaks\": {{\"on\": {breaksOn}, \"off\": {breaksOff}}}, \"plan\": {{{counts}}}, " +
                $"\"on\": {{\"batches\": {on[0]}, \"drawCalls\": {on[1]}, \"setPassCalls\": {on[2]}}}, \"off\": {{\"batches\": {off[0]}, \"drawCalls\": {off[1]}, \"setPassCalls\": {off[2]}}}, " +
                "\"source\": \"ProfilerRecorder Render 'Batches Count'/'Draw Calls Count'/'SetPass Calls Count' (last of 10 frames) with the UI canvas rendered through a temporary Screen Space - Camera camera; -1 = recorder unavailable; Editor PlayMode, not a device\"}\n");
            // Structural bound, pinned for the canonical new prototype estate at the default view (#372). A change here means the
            // decoration or building layering changed its batch cost: look at estate-decor-batches.json before re-pinning.
            // Checked at the end of the flow, so a changed count still leaves every capture of the run behind for review.
            TestContext.WriteLine($"materialBreaks on={breaksOn} off={breaksOff}");
            measuredBreaks = (breaksOn, breaksOff);
        }

        (int On, int Off) measuredBreaks;

        void CheckBatchBudget(string version, string state)
        {
            var graphics = View().GetComponentsInChildren<Graphic>().Where(g => g.isActiveAndEnabled).ToArray();
            int WithoutLandmarks(bool omitSites = false)
            {
                Material prior = null; Texture texture = null; var count = 0;
                foreach (var graphic in graphics)
                {
                    if (graphic.name.StartsWith("EstateMap.Landmark:", StringComparison.Ordinal)) continue;
                    if (omitSites && graphic.name.StartsWith("EstateMap.Site:", StringComparison.Ordinal)) continue;
                    var material = graphic.materialForRendering;
                    if (count == 0 || material != prior || texture != graphic.mainTexture)
                    { count++; prior = material; texture = graphic.mainTexture; }
                }
                return count;
            }
            var actual = EstateMapView.MaterialBreaks(View().transform); var baseline = WithoutLandmarks();
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-234/editor-meta"));
            Directory.CreateDirectory(output);
            var transitions = new System.Text.StringBuilder("index,layer,name,material,texture,newRun,isLandmark,isLv0\n");
            Material previousMaterial = null; Texture previousTexture = null;
            for (var index = 0; index < graphics.Length; index++)
            {
                var graphic = graphics[index]; var material = graphic.materialForRendering; var texture = graphic.mainTexture;
                string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
                transitions.AppendLine($"{index},{Quote(graphic.transform.parent?.name)},{Quote(graphic.name)},{Quote(material?.name)},{Quote(texture?.name)},{(index == 0 || material != previousMaterial || texture != previousTexture ? 1 : 0)},{graphic.name.StartsWith("EstateMap.Landmark:", StringComparison.Ordinal)},{graphic.name.StartsWith("EstateMap.Site:", StringComparison.Ordinal)}");
                previousMaterial = material; previousTexture = texture;
            }
            File.WriteAllText(Path.Combine(output, $"batch-runs-{version}-{state}.csv"), transitions.ToString());
            File.WriteAllText(Path.Combine(output, $"batch-{version}-{state}.csv"),
                $"version,decoration,actualDecorEnabled,base,landmarks,total,withoutLv0Parts,lv0PartsDelta\n{version},{state},{app.EstateMap.DecorEnabled},{baseline},{actual - baseline},{actual},{WithoutLandmarks(true)},{baseline - WithoutLandmarks(true)}\n");
            Debug.Log($"Estate batch budget {version}/{state}: base={baseline}, landmarks={actual - baseline}, total={actual}");
            Assert.That(app.EstateMap.DecorEnabled, Is.EqualTo(state == "on"), "Batch label must match the actual decoration switch.");
            Assert.That(actual - baseline, Is.LessThanOrEqualTo(4), "Landmarks must not alternate atlases by Y.");
            if (version == "m3a-prototype-r6" || version == "m3a-prototype-r7")
                Assert.That(baseline, Is.LessThanOrEqualTo(state == "on" ? 17 : 9), "Base drawing budget excludes landmarks.");
        }

        // Overlay canvases are not counted by the render counters, so the measurement renders the same canvas through a
        // temporary camera (Screen Space - Camera) for its frames and restores the overlay afterwards.
        IEnumerator Batches(int[] into)
        {
            var canvas = app.Ui.Canvas; var mode = canvas.renderMode;
            var host = new GameObject("Batch measurement camera"); var camera = host.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 1 << canvas.gameObject.layer;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
            try { yield return Count(into); }
            finally { canvas.renderMode = mode; canvas.worldCamera = null; Object.Destroy(host); }
            yield return null; Canvas.ForceUpdateCanvases();
        }

        static IEnumerator Count(int[] into)
        {
            using var batches = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Batches Count");
            using var draws = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count");
            using var setPass = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "SetPass Calls Count");
            for (var i = 0; i < 10; i++) yield return null;
            into[0] = batches.Valid ? (int)batches.LastValue : -1; into[1] = draws.Valid ? (int)draws.LastValue : -1;
            into[2] = setPass.Valid ? (int)setPass.LastValue : -1;
        }

        // Choose real vacant open ground from the current layout; historical fixed r6 coordinates are not r7 destinations.
        static Vector2Int VacantCell(EstateMapScreen screen, int width, int height, Vector2 awayFrom)
        {
            var candidates = new List<Vector2Int>();
            foreach (var zone in screen.Config.Zones.Where(screen.Open))
                for (var x = zone.X; x + width <= zone.X + zone.Width; x++)
                    for (var y = zone.Y; y + height <= zone.Y + zone.Height; y++)
                    {
                        var free = true;
                        for (var u = x; u < x + width; u++)
                            for (var v = y; v < y + height; v++)
                                if (screen.BuildingAt(new Vector2Int(u, v)) != null) free = false;
                        if (free) candidates.Add(new Vector2Int(x, y));
                    }
            Assert.That(candidates, Is.Not.Empty, "The layout must provide vacant ground for the move/selection flow.");
            return candidates.OrderByDescending(c => Vector2.SqrMagnitude(EstateMapModel.Project(c) - EstateMapModel.Project(awayFrom)))
                .ThenBy(c => c.x).ThenBy(c => c.y).First();
        }

        EstateMapView View() => app.Ui.GetComponentsInChildren<EstateMapView>().Single();
        RectTransform Named(string name, bool includeInactive) => app.Ui.GetComponentsInChildren<RectTransform>(includeInactive).Single(r => r.name == name);
        // #379 review: the same view with the far night at 0.75 instead of 0.88 [가설], for the user's side-by-side. The rules
        // object is the loaded one, so the value is restored before anything else draws.
        IEnumerator NightComparison(string name)
        {
            var night = EstateDecorRules.Load().night; var full = night.fullAlpha;
            try
            {
                night.fullAlpha = .75f; Click("EstateMap.Goal"); yield return null; Click("EstateMap.Goal"); yield return null;
                yield return Capture(name + "-night075");
            }
            finally { night.fullAlpha = full; }
            Click("EstateMap.Goal"); yield return null; Click("EstateMap.Goal"); yield return null;
        }

        // Steps the zoom buttons to a level.
        void ZoomTo(int level)
        {
            for (var guard = 0; app.EstateMap.Map.Zoom != level && guard < EstateMapModel.Levels; guard++)
            { Click(app.EstateMap.Map.Zoom < level ? "EstateMap.ZoomIn" : "EstateMap.ZoomOut"); Canvas.ForceUpdateCanvases(); }
            Assert.That(app.EstateMap.Map.Zoom, Is.EqualTo(level));
        }
        RectTransform[] Rects(string start) => app.Ui.GetComponentsInChildren<RectTransform>().Where(rect => rect.name.StartsWith(start, StringComparison.Ordinal)).ToArray();
        // Moves the camera the way a drag would, so the next tap lands inside the visible band.
        void Focus(Vector2 grid) { var view = View(); view.Model.CentreOn(grid); view.Layout(); Canvas.ForceUpdateCanvases(); }

        IEnumerator SetOption(bool on)
        {
            Click("Estate.Settings"); yield return ScreenIs("Settings");
            yield return Reveal(Toggle);
            Assert.That(Named(Toggle).GetComponentInChildren<Text>().text, Does.EndWith(on ? "꺼짐" : "켜짐"));
            Click(Toggle); yield return null;
            Click("Settings.Back");
        }

        void Tap(Vector2Int cell)
        {
            var view = app.Ui.GetComponentsInChildren<EstateMapView>().Single(); var rect = (RectTransform)view.transform;
            var local = view.Model.GridToViewport(new Vector2(cell.x + .5f, cell.y + .5f));
            var bounds = Pixels(rect); var scale = app.Ui.Canvas.scaleFactor;
            var point = new Vector2(bounds.xMin + local.x * scale, bounds.yMax - local.y * scale);
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = 32, position = point };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject, Is.SameAs(view.gameObject), "A tap reaches the map itself at " + point + ": " + hits[0].gameObject.name);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        static void Drag(RectTransform target, Vector2 from, Vector2 to)
        {
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = 33, position = from, pressPosition = from };
            ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.beginDragHandler);
            for (var step = 1; step <= 4; step++)
            {
                pointer.position = Vector2.Lerp(from, to, step / 4f); pointer.dragging = true;
                ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.dragHandler);
            }
            ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.endDragHandler);
        }

        IEnumerator Capture(string name)
        {
            yield return null; Canvas.ForceUpdateCanvases();
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-234/editor-meta")); Directory.CreateDirectory(output);
            var path = Path.Combine(output, name + "-" + Screen.width + "x" + Screen.height + ".png");
            // Copy pixels synchronously before the next UI action can redraw/destroy this page. File existence from
            // CaptureScreenshot is not a frame-completion fence and cannot bind the old PNG to current screen metadata.
            var waitStarted = Time.realtimeSinceStartup;
            var compilingAtWait = ShaderUtil.anythingCompiling;
            var compilingBefore = ShaderUtil.anythingCompiling;
            Debug.Log($"M3A7_CAPTURE before name={name} mode={ShaderProbeMode} anythingCompiling={compilingBefore} async={ShaderUtil.allowAsyncCompilation} frame={Time.frameCount}");
            yield return PlayModeCaptureGuard.WaitForShaders(name); var captureProbe = PlayModeCaptureGuard.Begin(name);
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            var compilingAfter = ShaderUtil.anythingCompiling;
            try
            {
                File.WriteAllBytes(path, image.EncodeToPNG());
                var cyan = image.GetPixels32().Count(c => c.r < 16 && c.g > 230 && c.b > 230);
                var probe = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/m3a7-shader-" + ShaderProbeMode)); Directory.CreateDirectory(probe);
                var csv = Path.Combine(probe, "captures.csv");
                if (!File.Exists(csv)) File.WriteAllText(csv, "name,frame,screen,async,compilingAtWait,compilingBefore,compilingAfter,elapsedThroughCaptureSeconds,cyanPixels\n");
                File.AppendAllText(csv, $"{name},{Time.frameCount},{app.CurrentScreenName},{ShaderUtil.allowAsyncCompilation},{compilingAtWait},{compilingBefore},{compilingAfter},{(Time.realtimeSinceStartup-waitStarted).ToString(System.Globalization.CultureInfo.InvariantCulture)},{cyan}\n");
                File.WriteAllBytes(Path.Combine(probe, name + ".png"), image.EncodeToPNG());
                PlayModeCaptureGuard.Check(image, captureProbe, path, compilingAfter);
                Assert.That(cyan, Is.Zero, "Cyan capture regression: " + name);
            }
            finally { Object.Destroy(image); }
            File.WriteAllText(path + ".txt", $"surface=actual Unity Editor PlayMode UI; mobileAspectSimulation=true; physicalDevice=false; userAcceptance=false; automatedPointerActions=true; screen={app.CurrentScreenName}; option=estatePrototype(development); zoom={app.EstateMap.Map.Zoom}; commit={BuildIdentity.Commit}; sourceDirty={BuildIdentity.SourceDirty}; dataHash={CanonicalContent.DataHash}; projection=isometric 64x32; art=Astra estate terrain, building and decor atlases\n");
        }

        IEnumerator Boot()
        {
            yield return SceneManager.LoadSceneAsync("Boot"); var deadline = Time.realtimeSinceStartup + 45;
            while ((app = Object.FindFirstObjectByType<RunCoordinator>()) == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(app, Is.Not.Null); yield return null; Assert.That(app.Error, Is.Null);
            app.Ui.SetPortraitMobileSimulation(true); app.Ui.SynchronizeViewport(); Canvas.ForceUpdateCanvases(); yield return null;
            yield return ScreenIs("Title");
        }
        IEnumerator ScreenIs(string name)
        {
            var deadline = Time.realtimeSinceStartup + 20;
            while (app.CurrentScreenName != name && app.Error == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(app.Error, Is.Null); Assert.That(app.CurrentScreenName, Is.EqualTo(name));
            yield return null; Canvas.ForceUpdateCanvases();
        }
        Button[] Buttons(string start) => app.Ui.GetComponentsInChildren<Button>().Where(button => button.gameObject.activeInHierarchy && button.name.StartsWith(start, StringComparison.Ordinal)).ToArray();
        Button Named(string name) => Buttons(name).Single(button => button.name == name);
        void Click(string name)
        {
            Canvas.ForceUpdateCanvases(); var button = Named(name); Assert.That(button.interactable, Is.True, name);
            var point = Pixels((RectTransform)button.transform).center;
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = 31, position = point };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0), name);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(button), name + " blocked at " + point + " by " + hits[0].gameObject.name);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        IEnumerator Reveal(string name)
        {
            var button = Named(name); var scroll = button.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.viewport == null) yield break;
            for (var step = 0; step <= 40; step++)
            {
                scroll.verticalNormalizedPosition = 1 - step / 40f; yield return null; Canvas.ForceUpdateCanvases();
                if (Pixels(scroll.viewport).Contains(Pixels((RectTransform)button.transform).center)) yield break;
            }
            Assert.Fail("Unreachable scroll target " + name);
        }
        static Rect Pixels(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners.Min(point => point.x), corners.Min(point => point.y), corners.Max(point => point.x), corners.Max(point => point.y));
        }
    }
}
#endif
