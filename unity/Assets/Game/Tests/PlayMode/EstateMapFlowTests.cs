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
    public sealed class EstateMapFlowTests
    {
        const string Toggle = "Settings.영지 지도 시제품 (개발)";
        string directory, prefix, priorDirectory, priorPrefix;
        int priorWidth, priorHeight;
        RunCoordinator app;

        [UnitySetUp] public IEnumerator Setup()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            Assert.That(Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None), Is.Empty);
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
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            Assert.That(Buttons("Estate.Priority:").Length, Is.EqualTo(4), "The option is off by default: the manor page is unchanged.");
            var account = MetaSaveCodec.Encode(app.Progression.State);

            yield return SetOption(true); yield return ScreenIs("EstateMap");
            var screen = app.EstateMap; var config = CanonicalContent.CreateAccountEstateConfig();
            Assert.That(Rects("EstateMap.Building:").Length, Is.EqualTo(config.Buildings.Length));
            Assert.That(Buttons("EstateMap.Building:"), Is.Empty, "Buildings carry no rectangular buttons; taps use the diamond grid.");
            Assert.That(Rects("EstateMap.NameTag"), Is.Empty, "Names are hidden until a building is selected.");
            Assert.That(Buttons("Estate.Priority:"), Is.Empty);
            var expected = screen.State.Estate.Buildings.OrderBy(b => EstateMapModel.SortY(b.X, b.Y, 2, 2)).ThenBy(b => b.Id).Select(b => "EstateMap.Building:" + b.Id).ToArray();
            Assert.That(Rects("EstateMap.Building:").OrderBy(r => r.GetSiblingIndex()).Select(r => r.name).ToArray(), Is.EqualTo(expected), "Draw order is front corner y, then instance order.");
            Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.Neighbourhood));
            var step = screen.Map.GridToViewport(Vector2.right) - screen.Map.GridToViewport(Vector2.zero);
            Assert.That(Vector2.Distance(step, new Vector2(32, 16)), Is.LessThan(1e-3f), "Default cells are 64×32 diamonds.");
            yield return Capture("estate-map-neighbourhood");

            Click("EstateMap.ZoomOut"); yield return null; Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.WholeMap));
            yield return Capture("estate-map-whole");
            Click("EstateMap.ZoomIn"); Click("EstateMap.ZoomIn"); yield return null; Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.BuildingDetail));
            yield return Capture("estate-map-building");
            Click("EstateMap.ZoomIn"); yield return null; Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.BuildingDetail), "Three levels only.");
            Click("EstateMap.ZoomOut"); yield return null; Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.Neighbourhood));

            var viewport = (RectTransform)View().transform;
            var offset = screen.Map.Offset;
            Drag(viewport, Pixels(viewport).center, Pixels(viewport).center + new Vector2(-120, 0) * app.Ui.Canvas.scaleFactor);
            yield return null;
            Assert.That(screen.Map.Offset.x, Is.GreaterThan(offset.x + 100), "A one-finger drag left shows more of the east.");
            Drag(viewport, Pixels(viewport).center, Pixels(viewport).center + new Vector2(1000, 0) * app.Ui.Canvas.scaleFactor);
            yield return null;
            Assert.That(screen.Map.GridToViewport(new Vector2(0, 8)).x, Is.EqualTo(0).Within(1e-2f), "The drag stops at the west corner.");

            Click("EstateMap.Goal"); yield return null; Assert.That(screen.GoalCollapsed, Is.True);
            yield return Capture("estate-map-goal-folded");
            Click("EstateMap.Goal"); yield return null; Assert.That(screen.GoalCollapsed, Is.False);

            var forge = screen.State.Estate.Buildings.Single(b => b.Kind == "meta:forge");
            Focus(screen.Centre(forge.Id)); Tap(new Vector2Int(forge.X + 1, forge.Y + 1)); yield return null;
            Assert.That(screen.Selected, Is.EqualTo(forge.Id), "A tap on any cell of the footprint selects the building.");
            Assert.That(Rects("EstateMap.NameTag").Length, Is.EqualTo(1), "Only the selected building shows its name.");
            Assert.That(Named("EstateMap.Move").interactable, Is.True);
            Assert.That(screen.Map.GridToViewport(Vector2.zero).y, Is.GreaterThanOrEqualTo(screen.Map.TopInset - .5f));
            Assert.That(screen.Map.GridToViewport(new Vector2(16, 8)).y, Is.LessThanOrEqualTo(screen.Map.Viewport.y - screen.Map.BottomInset + .5f), "The panel never hides a row.");
            yield return Capture("estate-map-selected");
            Click("EstateMap.Move"); yield return null; Assert.That(screen.Moving, Is.True);
            // At building zoom the original footprint leaves the screen once the player pans to the destination; the redraw
            // after the tap must keep the destination preview in view (review on #369).
            Click("EstateMap.ZoomIn"); yield return null; Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.BuildingDetail));
            Focus(new Vector2(4.5f, 4.5f)); Tap(new Vector2Int(4, 4)); yield return null;
            Assert.That(screen.Preview, Is.EqualTo(new Vector2Int(4, 4))); Assert.That(screen.PreviewValid, Is.True, screen.PreviewReason);
            Assert.That(screen.Map.InView(new Vector2(5, 5)), Is.True, "The destination preview stays on screen after the tap redraws the page.");
            Assert.That(screen.Map.InView(screen.Centre(forge.Id)), Is.False, "The camera did not jump back to the original footprint.");
            Click("EstateMap.ZoomOut"); yield return null;
            yield return Capture("estate-map-move-valid");
            Click("EstateMap.Confirm"); yield return null;
            Assert.That(screen.Moving, Is.False);
            Assert.That(screen.Building(forge.Id).X, Is.EqualTo(4)); Assert.That(screen.Building(forge.Id).Y, Is.EqualTo(4));

            var home = screen.State.Estate.Buildings.Single(b => b.Kind == "m3a:home");
            Click("EstateMap.Move"); yield return null;
            Focus(screen.Centre(home.Id)); Tap(new Vector2Int(home.X, home.Y)); yield return null;
            Assert.That(screen.PreviewValid, Is.False); Assert.That(screen.PreviewReason, Is.EqualTo("overlaps:" + home.Id));
            Assert.That(Named("EstateMap.Confirm").interactable, Is.False);
            yield return Capture("estate-map-move-blocked");
            Click("EstateMap.Cancel"); yield return null;
            Assert.That(screen.Building(forge.Id).X, Is.EqualTo(4), "Cancel keeps the last confirmed place.");
            Focus(new Vector2(12, 4)); Tap(new Vector2Int(12, 4)); yield return null;
            Assert.That(screen.Selected, Is.Null, "A tap on empty ground closes the selection.");

            Assert.That(app.Progression.State.Estate, Is.Null);
            Assert.That(MetaSaveCodec.Encode(app.Progression.State), Is.EqualTo(account), "The prototype only reads the account.");
            yield return SetOption(false); yield return ScreenIs("Estate");
            Assert.That(Buttons("Estate.Priority:").Length, Is.EqualTo(4));
        }

        EstateMapView View() => app.Ui.GetComponentsInChildren<EstateMapView>().Single();
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
            if (File.Exists(path)) File.Delete(path); ScreenCapture.CaptureScreenshot(path); var deadline = Time.realtimeSinceStartup + 10;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(File.Exists(path), Is.True, path);
            File.WriteAllText(path + ".txt", $"surface=actual Unity Editor PlayMode UI; mobileAspectSimulation=true; physicalDevice=false; userAcceptance=false; automatedPointerActions=true; screen={app.CurrentScreenName}; option=estatePrototype(development); zoom={app.EstateMap.Map.Zoom}; commit={BuildIdentity.Commit}; sourceDirty={BuildIdentity.SourceDirty}; dataHash={CanonicalContent.DataHash}; projection=isometric 64x32; art=temporary manor sprites and solid colour blocks\n");
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
