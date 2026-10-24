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
        public IEnumerator DevelopmentOptionSwitchesTheEstateTabToTheMapAndBack()
        {
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            Assert.That(Buttons("Estate.Priority:").Length, Is.EqualTo(4), "The option is off by default: the manor page is unchanged.");
            var account = MetaSaveCodec.Encode(app.Progression.State);

            yield return SetOption(true); yield return ScreenIs("EstateMap");
            var screen = app.EstateMap; var config = CanonicalContent.CreateAccountEstateConfig();
            Assert.That(Buttons("EstateMap.Building:").Length, Is.EqualTo(config.Buildings.Length));
            Assert.That(Buttons("Estate.Priority:"), Is.Empty);
            Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.Neighbourhood));
            yield return Capture("estate-map-neighbourhood");

            Click("EstateMap.ZoomOut"); yield return null; Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.WholeMap));
            yield return Capture("estate-map-whole");
            Click("EstateMap.ZoomIn"); Click("EstateMap.ZoomIn"); yield return null; Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.BuildingDetail));
            Click("EstateMap.ZoomIn"); yield return null; Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.BuildingDetail), "Three levels only.");
            Click("EstateMap.ZoomOut"); yield return null; Assert.That(screen.Map.Zoom, Is.EqualTo(EstateMapModel.Neighbourhood));

            var viewport = (RectTransform)app.Ui.transform.GetComponentsInChildren<EstateMapView>().Single().transform;
            var offset = screen.Map.Offset;
            Drag(viewport, Pixels(viewport).center, Pixels(viewport).center + new Vector2(-120, 0) * app.Ui.Canvas.scaleFactor);
            yield return null;
            Assert.That(screen.Map.Offset.x, Is.GreaterThan(offset.x + 100), "A one-finger drag left shows more of the east.");
            Drag(viewport, Pixels(viewport).center, Pixels(viewport).center + new Vector2(400, 0) * app.Ui.Canvas.scaleFactor);
            yield return null;
            Assert.That(screen.Map.Offset.x, Is.EqualTo(0).Within(1e-3f), "The drag stops at the west edge.");

            var forge = screen.State.Estate.Buildings.Single(b => b.Kind == "meta:forge");
            Click("EstateMap.Building:" + forge.Id); yield return null;
            Assert.That(screen.Selected, Is.EqualTo(forge.Id));
            Assert.That(Named("EstateMap.Move").interactable, Is.True);
            yield return Capture("estate-map-selected");
            Click("EstateMap.Move"); yield return null; Assert.That(screen.Moving, Is.True);
            Tap(new Vector2Int(4, 4)); yield return null;
            Assert.That(screen.Preview, Is.EqualTo(new Vector2Int(4, 4))); Assert.That(screen.PreviewValid, Is.True, screen.PreviewReason);
            yield return Capture("estate-map-move-valid");
            Click("EstateMap.Confirm"); yield return null;
            Assert.That(screen.Moving, Is.False);
            Assert.That(screen.Building(forge.Id).X, Is.EqualTo(4)); Assert.That(screen.Building(forge.Id).Y, Is.EqualTo(4));

            var home = screen.State.Estate.Buildings.Single(b => b.Kind == "m3a:home");
            Click("EstateMap.Move"); yield return null;
            Tap(new Vector2Int(home.X, home.Y)); yield return null;
            Assert.That(screen.PreviewValid, Is.False); Assert.That(screen.PreviewReason, Is.EqualTo("overlaps:" + home.Id));
            Assert.That(Named("EstateMap.Confirm").interactable, Is.False);
            yield return Capture("estate-map-move-blocked");
            Click("EstateMap.Cancel"); yield return null;
            Assert.That(screen.Moving, Is.False);
            Assert.That(screen.Building(forge.Id).X, Is.EqualTo(4), "Cancel keeps the last confirmed place.");
            Click("EstateMap.Close"); yield return null; Assert.That(screen.Selected, Is.Null);

            Assert.That(app.Progression.State.Estate, Is.Null);
            Assert.That(MetaSaveCodec.Encode(app.Progression.State), Is.EqualTo(account), "The prototype only reads the account.");
            yield return SetOption(false); yield return ScreenIs("Estate");
            Assert.That(Buttons("Estate.Priority:").Length, Is.EqualTo(4));
        }

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
            Assert.That(hits[0].gameObject, Is.SameAs(view.gameObject), "A move-mode tap reaches the map, not a building, at " + point + ": " + hits[0].gameObject.name);
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
            File.WriteAllText(path + ".txt", $"surface=actual Unity Editor PlayMode UI; mobileAspectSimulation=true; physicalDevice=false; userAcceptance=false; automatedPointerActions=true; screen={app.CurrentScreenName}; option=estatePrototype(development); zoom={app.EstateMap.Map.Zoom}; commit={BuildIdentity.Commit}; sourceDirty={BuildIdentity.SourceDirty}; dataHash={CanonicalContent.DataHash}; art=temporary manor sprites and solid colour plates\n");
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
