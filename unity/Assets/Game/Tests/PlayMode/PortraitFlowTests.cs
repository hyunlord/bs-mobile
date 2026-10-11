#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.App;
using Game.App.Generated;
using Game.View;
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
    public sealed class PortraitFlowTests
    {
        string previousMetaDirectory, previousPreferencePrefix, directory, preferencePrefix;
        int previousWidth, previousHeight;
        RunCoordinator app;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Portrait flow requires the wave-1a export.");
            Assert.That(Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None), Is.Empty,
                "A previous Boot instance must not contaminate the isolated flow.");
            previousMetaDirectory = RunCoordinator.MetaDirectoryOverride;
            previousPreferencePrefix = RunCoordinator.PreferencePrefixOverride;
            previousWidth = Screen.width; previousHeight = Screen.height;
            var identity = Guid.NewGuid().ToString("N");
            directory = Path.Combine(Path.GetTempPath(), "sowsiege-portrait-flow-" + identity);
            preferencePrefix = "sowsiege.test.portrait." + identity + ".";
            RunCoordinator.MetaDirectoryOverride = directory;
            RunCoordinator.PreferencePrefixOverride = preferencePrefix;
            PlayModeWindow.SetCustomRenderingResolution(390, 844, "Portrait flow QA");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var boot in Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None)) Object.Destroy(boot.gameObject);
            yield return null;
            if (directory != null)
            {
                RunCoordinator.MetaDirectoryOverride = previousMetaDirectory;
                RunCoordinator.PreferencePrefixOverride = previousPreferencePrefix;
                foreach (var key in RunPreferences.Keys) PlayerPrefs.DeleteKey(preferencePrefix + key);
                PlayerPrefs.Save();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                PlayModeWindow.SetCustomRenderingResolution((uint)previousWidth, (uint)previousHeight, "Restored test viewport");
            }
            app = null;
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator FirstDepartureThenRepeatTitleAndConfirmedAbandonUseActualRoutes()
        {
            yield return Boot();
            Assert.That(app.CurrentScreenName, Is.EqualTo("Title"));
            Assert.That(app.Session, Is.Null);
            Assert.That(RunPreferences.Load(preferencePrefix).IntroductionSeen, Is.False);
            Click("Title.Settings");
            yield return WaitForScreen("Settings");
            app.NavigateBack();
            yield return WaitForScreen("Title");
            Click("Title.Enter");
            yield return WaitForScreen("Run");
            Assert.That(app.Session, Is.Not.Null);
            Assert.That(RunPreferences.Load(preferencePrefix).IntroductionSeen, Is.True);
            Assert.That(Path.GetFullPath(app.RecordedReplayPath), Does.StartWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar));
            var session = app.Session;
            app.NavigateBack();
            yield return WaitForScreen("Pause");
            var tick = app.Frame.Tick;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(app.Frame.Tick, Is.EqualTo(tick));
            Click("Pause.Abandon");
            yield return WaitForScreen("ConfirmAbandon");
            Click("Abandon.Cancel");
            yield return WaitForScreen("Pause");
            Assert.That(app.Session, Is.SameAs(session));
            Assert.That(app.Frame.Tick, Is.EqualTo(tick));
            Click("Pause.Continue");
            yield return WaitForScreen("Run");
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(app.Frame.Tick, Is.GreaterThan(tick));
            app.EditorCaptureNotifyFocus(false);
            yield return WaitForScreen("Pause");
            tick = app.Frame.Tick;
            app.EditorCaptureNotifyFocus(true);
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(app.CurrentScreenName, Is.EqualTo("Pause"));
            Assert.That(app.Frame.Tick, Is.EqualTo(tick));
            app.NavigateBack();
            yield return WaitForScreen("Run");
            app.OpenPause();
            yield return WaitForScreen("Pause");
            Click("Pause.Abandon");
            yield return WaitForScreen("ConfirmAbandon");
            Click("Abandon.Confirm");
            yield return WaitForScreen("Summary");
            Click("Summary.Estate");
            yield return WaitForScreen("Estate");
            Assert.That(app.Error, Is.Null);
            foreach (var boot in Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None)) Object.Destroy(boot.gameObject);
            yield return null;
            yield return Boot();
            Click("Title.Enter");
            yield return WaitForScreen("Estate");
            Assert.That(app.Session, Is.Null, "A returning player must enter estate before starting another run.");
            app.NavigateBack();
            yield return WaitForScreen("ConfirmExit");
            app.NavigateBack();
            yield return WaitForScreen("Estate");
            Click("Estate.Depart");
            yield return WaitForScreen("Run");
            Assert.That(app.Session, Is.Not.Null);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator CardPointerGuardAndBackLifecyclePreserveTheUnresolvedOffer()
        {
            yield return Boot();
            Click("Title.Enter");
            yield return WaitForScreen("Run");
            app.Send(ReplayCommandKind.GrantLevel);
            yield return WaitForScreen("Cards");
            var offered = app.Session.View.CaptureCards();
            var sequence = app.Session.NextSequence;
            var tick = app.Frame.Tick;
            var guardCapture = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-flow-225/editor/guard-offer-before-pointer.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(guardCapture));
            if (File.Exists(guardCapture)) File.Delete(guardCapture);
            ScreenCapture.CaptureScreenshot(guardCapture);
            yield return null;
            Assert.That(ButtonNamed("Card.Choose:" + offered.Cards[0]).interactable, Is.False, "Capture preparation must remain inside the 300 ms guard.");
            Click("Card.Choose:" + offered.Cards[0], allowDisabled: true);
            Assert.That(app.Session.NextSequence, Is.EqualTo(sequence), "Moving-pointer release must not select during the 300 ms guard.");
            Assert.That(app.Frame.Status, Is.EqualTo(RunStatus.AwaitingCard));
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(app.Frame.Tick, Is.EqualTo(tick));
            Assert.That(offered.Bans, Is.GreaterThan(0), "Canonical opening offer must exercise a real ban allowance.");
            yield return Reveal("Card.Ban:" + offered.Cards[0]);
            Click("Card.Ban:" + offered.Cards[0]);
            yield return null;
            Assert.That(app.Frame.Status, Is.EqualTo(RunStatus.AwaitingCard), "Secondary ban must never bubble into card selection.");
            Assert.That(app.Session.View.CaptureCards().Bans, Is.EqualTo(offered.Bans - 1));
            Assert.That(app.Session.NextSequence, Is.EqualTo(sequence + 1));
            var remaining = app.Session.View.CaptureCards().Cards.ToArray();
            app.NavigateBack();
            yield return WaitForScreen("Pause");
            Click("Pause.Settings");
            yield return WaitForScreen("Settings");
            app.NavigateBack();
            yield return WaitForScreen("Pause");
            app.ContinuePausedRun();
            yield return WaitForScreen("Cards");
            Assert.That(app.Session.View.CaptureCards().Cards, Is.EqualTo(remaining));
            Assert.That(app.Frame.Tick, Is.EqualTo(tick));
            app.EditorCaptureNotifyPause(true);
            app.EditorCaptureNotifyFocus(false);
            yield return null;
            app.EditorCaptureNotifyPause(false);
            app.EditorCaptureNotifyFocus(true);
            yield return WaitForScreen("Pause");
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(app.Frame.Tick, Is.EqualTo(tick), "Foreground restoration must not resolve a card or resume simulation.");
            Assert.That(app.Session.View.CaptureCards().Cards, Is.EqualTo(remaining));
            Click("Pause.Continue");
            yield return WaitForScreen("Cards");
            yield return new WaitForSecondsRealtime(.35f);
            yield return Reveal("Card.Choose:" + remaining[0]);
            Click("Card.Choose:" + remaining[0]);
            yield return WaitForScreen("Run");
            Assert.That(app.Frame.Status, Is.EqualTo(RunStatus.Running));
            Assert.That(app.Error, Is.Null);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator ActualPortraitScreensCaptureReferencePhoneRatioAndCenteredMac()
        {
            yield return Boot();
            yield return CaptureSizes("title", false);
            Click("Title.Enter");
            yield return WaitForScreen("Run");
            yield return CaptureSizes("run", false);
            app.Send(ReplayCommandKind.GrantLevel);
            yield return WaitForScreen("Cards");
            yield return new WaitForSecondsRealtime(.35f);
            yield return CaptureSizes("cards", true);
            app.NavigateBack();
            yield return WaitForScreen("Pause");
            yield return CaptureSizes("pause", true);
            Click("Pause.Settings");
            yield return WaitForScreen("Settings");
            yield return CaptureSizes("settings", true);
            Click("Settings.Back");
            yield return WaitForScreen("Pause");
            Click("Pause.Abandon");
            yield return WaitForScreen("ConfirmAbandon");
            yield return CaptureSizes("abandon-confirm", true);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator RetryingWithTheSameCommandSequenceStartsANewCardInputGuard()
        {
            yield return Boot();
            Click("Title.Enter");
            yield return WaitForScreen("Run");
            PrepareSameSequenceOffer();
            yield return WaitForScreen("Cards");
            var originalSession = app.Session;
            var originalSequence = app.Session.NextSequence;
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(ButtonNamed("Card.Choose:" + app.Session.View.CaptureCards().Cards[0]).interactable, Is.True);
            app.OpenPause();
            yield return WaitForScreen("Pause");
            Click("Pause.Abandon");
            yield return WaitForScreen("ConfirmAbandon");
            Click("Abandon.Confirm");
            yield return WaitForScreen("Summary");
            Click("Summary.Retry");
            yield return WaitForScreen("Run");
            Assert.That(app.Session, Is.Not.SameAs(originalSession));
            PrepareSameSequenceOffer();
            yield return WaitForScreen("Cards");
            Assert.That(app.Session.NextSequence, Is.EqualTo(originalSequence), "Exercise the same sequence number in a different session.");
            var choice = "Card.Choose:" + app.Session.View.CaptureCards().Cards[0];
            Assert.That(ButtonNamed(choice).interactable, Is.False, "An expired guard from the previous session must not unlock this offer.");
            Click(choice, allowDisabled: true);
            Assert.That(app.Session.NextSequence, Is.EqualTo(originalSequence));
            Assert.That(app.Frame.Status, Is.EqualTo(RunStatus.AwaitingCard));
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(ButtonNamed(choice).interactable, Is.True);
        }

        void PrepareSameSequenceOffer()
        {
            app.OpenPause();
            Assert.That(app.Session.NextSequence, Is.LessThanOrEqualTo(20));
            while (app.Session.NextSequence < 20) app.Send(ReplayCommandKind.Advance);
            app.Send(ReplayCommandKind.GrantLevel);
            app.ContinuePausedRun();
        }

        IEnumerator Boot()
        {
            yield return SceneManager.LoadSceneAsync("Boot");
            var deadline = Time.realtimeSinceStartup + 45;
            while ((app = Object.FindFirstObjectByType<RunCoordinator>()) == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(app, Is.Not.Null);
            yield return null;
            Assert.That(app.Error, Is.Null);
            Assert.That(app.Ui.PortraitEnabled, Is.True);
            app.Ui.SetPortraitMobileSimulation(true);
            app.Ui.SynchronizeViewport();
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        IEnumerator WaitForScreen(string expected)
        {
            var deadline = Time.realtimeSinceStartup + 20;
            while (app.CurrentScreenName != expected && app.Error == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(app.Error, Is.Null);
            Assert.That(app.CurrentScreenName, Is.EqualTo(expected));
            Canvas.ForceUpdateCanvases();
        }

        Button ButtonNamed(string name)
        {
            var matches = app.Ui.GetComponentsInChildren<Button>().Where(button => button.gameObject.activeInHierarchy && button.name == name).ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), "Expected one active button: " + name);
            return matches[0];
        }

        void Click(string name, bool allowDisabled = false)
        {
            Canvas.ForceUpdateCanvases();
            var button = ButtonNamed(name);
            if (!allowDisabled) Assert.That(button.interactable, Is.True, name);
            var rect = (RectTransform)button.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerId = 19, position = point };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0), name + " must receive a real UI raycast.");
            var actual = hits[0].gameObject.GetComponentInParent<Button>();
            var scroll = button.GetComponentInParent<ScrollRect>();
            var geometry = $"button={name}; pointer={point}; buttonPixels={Pixels(rect)}; scrollPixels={(scroll != null && scroll.viewport != null ? Pixels(scroll.viewport).ToString() : "none")}; safe={app.Ui.SafePixels}; hits={string.Join(" | ", hits.Select(hit => hit.gameObject.name))}";
            if (actual != button)
            {
                var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-flow-225/editor"));
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "pointer-obstruction.txt"), geometry);
            }
            Assert.That(actual, Is.SameAs(button), name + " is obstructed by another UI target. " + geometry);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        IEnumerator Reveal(string name)
        {
            var button = ButtonNamed(name);
            var scroll = button.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.viewport == null) yield break;
            for (var step = 0; step <= 20; step++)
            {
                scroll.verticalNormalizedPosition = 1 - step / 20f;
                yield return null;
                Canvas.ForceUpdateCanvases();
                if (Pixels(scroll.viewport).Contains(Pixels((RectTransform)button.transform).center)) yield break;
            }
            Assert.Fail("Button center cannot be reached by scrolling: " + name);
        }

        IEnumerator CaptureSizes(string screen, bool cardFixture)
        {
            foreach (var size in new[] { new Vector2Int(390, 844), new Vector2Int(1080, 2520), new Vector2Int(1920, 1080) })
            {
                var mobile = size.x < size.y;
                PlayModeWindow.SetCustomRenderingResolution((uint)size.x, (uint)size.y, "Portrait flow QA");
                yield return null; yield return null;
                app.Ui.SetPortraitMobileSimulation(mobile);
                app.Ui.SynchronizeViewport();
                yield return null; yield return null;
                yield return new WaitForSecondsRealtime(.2f);
                Canvas.ForceUpdateCanvases();
                Assert.That(Screen.width, Is.EqualTo(size.x));
                Assert.That(Screen.height, Is.EqualTo(size.y));
                Assert.That(app.CurrentScreenName, Is.EqualTo(screen == "abandon-confirm" ? "ConfirmAbandon" : char.ToUpperInvariant(screen[0]) + screen.Substring(1)), "Capture must not silently record another route.");
                AssertViewport(mobile);
                AssertTouchTargets();
                if (screen == "run") AssertHudCoverage(size.x == 390);
                if (screen == "pause") AssertPauseScrollDoesNotCoverActions();
                yield return Capture(screen, size, mobile, cardFixture);
                if (screen == "cards" || screen == "settings" || screen == "pause")
                {
                    var scroll = app.Ui.GetComponentInChildren<ScrollRect>();
                    Assert.That(scroll, Is.Not.Null);
                    scroll.verticalNormalizedPosition = 0;
                    yield return null; yield return null;
                    Canvas.ForceUpdateCanvases();
                    AssertTouchTargets();
                    if (screen == "pause") AssertPauseScrollDoesNotCoverActions();
                    yield return Capture(screen + "-bottom", size, mobile, cardFixture);
                    scroll.verticalNormalizedPosition = 1;
                    yield return null;
                }
            }
        }

        IEnumerator Capture(string screen, Vector2Int size, bool mobile, bool cardFixture)
        {
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-flow-225/editor"));
            Directory.CreateDirectory(output);
            var path = Path.Combine(output, screen + "-" + size.x + "x" + size.y + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            var deadline = Time.realtimeSinceStartup + 10;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(File.Exists(path), Is.True, path);
            var image = new Texture2D(2, 2);
            try
            {
                Assert.That(image.LoadImage(File.ReadAllBytes(path)), Is.True);
                Assert.That(image.width, Is.EqualTo(size.x));
                Assert.That(image.height, Is.EqualTo(size.y));
            }
            finally { Object.Destroy(image); }
            if (screen.StartsWith("cards", StringComparison.Ordinal) || screen.StartsWith("pause", StringComparison.Ordinal))
            {
                var panelName = screen.StartsWith("pause", StringComparison.Ordinal) ? "Portrait.Pause" : "Portrait.Cards";
                var panel = app.Ui.GetComponentsInChildren<RectTransform>().Single(rect => rect.name == panelName);
                PortraitTextGeometry.Write(panel, path + ".geometry.json");
            }
            File.WriteAllText(path + ".txt", $"surface=actual Unity Editor PlayMode UI; mobileAspectSimulation={mobile}; physicalDevice=false; userAcceptance=false; screen={app.CurrentScreenName}; viewport={app.Ui.PortraitPixels}; safePixels={app.Ui.SafePixels}; commit={BuildIdentity.Commit}; sourceHash={BuildIdentity.SourceHash}; sourceDirty={BuildIdentity.SourceDirty}; dataHash={CanonicalContent.DataHash}; tick={app.Frame?.Tick}; automatedPointerActions=true; developerGrantLevelFixture={cardFixture}; normalPlayEvidence=false\n");
        }

        void AssertViewport(bool mobile)
        {
            var viewport = app.Ui.PortraitPixels;
            var safe = app.Ui.SafePixels;
            Assert.That(viewport.center.x, Is.EqualTo(Screen.width * .5f).Within(1));
            Assert.That(viewport.center.y, Is.EqualTo(Screen.height * .5f).Within(1));
            Assert.That(safe.xMin, Is.GreaterThanOrEqualTo(viewport.xMin - 1));
            Assert.That(safe.xMax, Is.LessThanOrEqualTo(viewport.xMax + 1));
            Assert.That(safe.yMin, Is.GreaterThanOrEqualTo(viewport.yMin - 1));
            Assert.That(safe.yMax, Is.LessThanOrEqualTo(viewport.yMax + 1));
            if (mobile) Assert.That(viewport.size, Is.EqualTo(new Vector2(Screen.width, Screen.height)));
            else
            {
                Assert.That(viewport.width / viewport.height, Is.EqualTo(390f / 844).Within(.001));
                Assert.That(viewport.xMin, Is.GreaterThan(0), "Mac leaves both sides outside the centered portrait game.");
            }
            if (app.CurrentScreenName == "Run")
            {
                Assert.That(Camera.main, Is.Not.Null);
                Assert.That(Camera.main.pixelRect.x, Is.EqualTo(viewport.x).Within(1));
                Assert.That(Camera.main.pixelRect.y, Is.EqualTo(viewport.y).Within(1));
                Assert.That(Camera.main.pixelRect.width, Is.EqualTo(viewport.width).Within(1));
                Assert.That(Camera.main.pixelRect.height, Is.EqualTo(viewport.height).Within(1));
            }
        }

        void AssertTouchTargets()
        {
            var scale = app.Ui.Canvas.scaleFactor;
            var safe = app.Ui.SafePixels;
            foreach (var target in app.Ui.GetComponentsInChildren<Selectable>())
            {
                if (!target.gameObject.activeInHierarchy) continue;
                var bounds = Pixels((RectTransform)target.transform);
                Assert.That(bounds.width / scale, Is.GreaterThanOrEqualTo(43.99f), target.name + " width");
                Assert.That(bounds.height / scale, Is.GreaterThanOrEqualTo(43.99f), target.name + " height");
                var scroll = target.GetComponentInParent<ScrollRect>();
                if (scroll != null && scroll.viewport != null)
                {
                    var clip = Pixels(scroll.viewport);
                    if (!clip.Overlaps(bounds)) continue;
                    bounds = Rect.MinMaxRect(Mathf.Max(bounds.xMin, clip.xMin), Mathf.Max(bounds.yMin, clip.yMin), Mathf.Min(bounds.xMax, clip.xMax), Mathf.Min(bounds.yMax, clip.yMax));
                }
                Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(safe.xMin - 1), target.name);
                Assert.That(bounds.xMax, Is.LessThanOrEqualTo(safe.xMax + 1), target.name);
                Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(safe.yMin - 1), target.name);
                Assert.That(bounds.yMax, Is.LessThanOrEqualTo(safe.yMax + 1), target.name);
            }
        }

        void AssertHudCoverage(bool requireHint)
        {
            var bands = app.Ui.GetComponentsInChildren<RectTransform>().Where(rect => rect.name == "HUD").ToArray();
            Assert.That(bands.Length, Is.EqualTo(1));
            var band = Pixels(bands[0]);
            Assert.That(band.height, Is.LessThanOrEqualTo(app.Ui.SafePixels.height * .1f + 1));
            Assert.That(band.yMax, Is.EqualTo(app.Ui.SafePixels.yMax).Within(1));
            var hints = app.Ui.GetComponentsInChildren<RectTransform>().Where(rect => rect.name == "Hint" && rect.gameObject.activeInHierarchy).ToArray();
            if (requireHint) Assert.That(hints.Length, Is.EqualTo(1), "Fresh departure must exercise the actual movement hint geometry.");
            var equipment = bands[0].GetComponentsInChildren<RectTransform>().Where(rect => rect.name.StartsWith("Equipment ", StringComparison.Ordinal) && rect.name != "Equipment pool" && rect.gameObject.activeInHierarchy).ToArray();
            foreach (var hint in hints)
            {
                var bounds = Pixels(hint);
                Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(band.yMin - 1), "Hint must remain within the reserved HUD band.");
                Assert.That(bounds.yMax, Is.LessThanOrEqualTo(band.yMax + 1));
                foreach (var slot in equipment)
                    Assert.That(bounds.Overlaps(Pixels(slot)), Is.False, "Hint covers equipped slot " + slot.name);
            }
        }

        void AssertPauseScrollDoesNotCoverActions()
        {
            var panel = app.Ui.GetComponentsInChildren<RectTransform>().Single(rect => rect.name == "Portrait.Pause");
            var scroll = panel.GetComponentInChildren<ScrollRect>();
            Assert.That(scroll, Is.Not.Null);
            var viewport = Pixels(scroll.viewport);
            var settings = Pixels((RectTransform)ButtonNamed("Pause.Settings").transform);
            Assert.That(viewport.yMin, Is.GreaterThanOrEqualTo(settings.yMax - .5f), "Pause body must be clipped above its fixed action buttons after every resize.");
        }

        static Rect Pixels(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners.Min(point => point.x), corners.Min(point => point.y), corners.Max(point => point.x), corners.Max(point => point.y));
        }
    }
}
#endif
