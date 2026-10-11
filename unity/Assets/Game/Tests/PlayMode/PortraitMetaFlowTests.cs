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
    public sealed class PortraitMetaFlowTests
    {
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
            directory = Path.Combine(Path.GetTempPath(), "sowsiege-meta-flow-" + id);
            prefix = "sowsiege.test.meta-flow." + id + ".";
            RunCoordinator.MetaDirectoryOverride = directory; RunCoordinator.PreferencePrefixOverride = prefix;
            PlayModeWindow.SetCustomRenderingResolution(390, 844, "Portrait meta flow QA");
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
        public IEnumerator FirstWaveRunSettlesThenActualMetaRoutesExposeCatalogAndLockedDeparture()
        {
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Run");
            Assert.That(app.Progression, Is.Not.Null);
            Assert.That(app.Progression.State.PendingRun, Is.Not.Null);
            Assert.That(app.Progression.Catalog.ManorBuildings.Length, Is.EqualTo(4));
            Assert.That(app.Progression.Catalog.Vassals.Length, Is.EqualTo(6));
            Assert.That(app.Progression.Catalog.Challenges.Length, Is.EqualTo(20));
            app.OpenPause(); yield return ScreenIs("Pause");
            var beforeCancel = app.Progression.State;
            Click("Pause.Abandon"); yield return ScreenIs("ConfirmAbandon");
            Click("Abandon.Cancel"); yield return ScreenIs("Pause");
            Assert.That(MetaSaveCodec.Encode(app.Progression.State), Is.EqualTo(MetaSaveCodec.Encode(beforeCancel)));
            Click("Pause.Abandon"); yield return ScreenIs("ConfirmAbandon");
            Click("Abandon.Confirm"); yield return ScreenIs("Summary");
            Assert.That(app.Progression.State.PendingRun, Is.Null);
            var completedRuns = app.Progression.State.CompletedRuns;
            Assert.That(completedRuns, Is.EqualTo(1));
            yield return CaptureSizes("result", "Summary", false);
            Click("Summary.Estate"); yield return ScreenIs("Estate");
            Assert.That(Buttons("Estate.Priority:").Length, Is.EqualTo(4));
            yield return CaptureSizes("estate", "Estate", false);
            Click("Estate.Settings"); yield return ScreenIs("Settings");
            app.NavigateBack(); yield return ScreenIs("Estate");
            Click("Tab.Vassals"); yield return ScreenIs("Vassals");
            Assert.That(Buttons("Vassals.Select:").Length, Is.EqualTo(6));
            yield return CaptureSizes("vassals", "Vassals", false);
            Click("Tab.Challenges"); yield return ScreenIs("Challenges");
            Assert.That(Buttons("Challenges.Claim:").Length, Is.EqualTo(20));
            foreach (var challenge in app.Progression.Catalog.Challenges)
                Assert.That(app.Ui.GetComponentsInChildren<Text>().Any(text => text.text == challenge.Name), Is.True, challenge.Id);
            yield return CaptureSizes("challenges", "Challenges", false);
            app.NavigateBack(); yield return ScreenIs("Estate");
            Click("Estate.Depart"); yield return ScreenIs("Departure");
            Assert.That(Buttons("Departure.Chapter:").Length, Is.EqualTo(7));
            var catalog = app.Progression.Catalog; var state = app.Progression.State;
            var slots = Math.Min(catalog.Economy.MaximumVassalSlots, catalog.Economy.BaseVassalSlots +
                catalog.ManorBuildings.Where(item => item.Effect == ManorEffect.VassalSlots).Sum(item => state.ManorLevels[item.Id]) / catalog.Economy.BarracksLevelsPerSlot);
            Assert.That(Buttons("Departure.VassalSlot:").Length, Is.EqualTo(3));
            for (var i = 0; i < 3; i++) Assert.That(Named("Departure.VassalSlot:" + i).interactable, Is.EqualTo(i < slots));
            Click("Departure.Chapter:" + catalog.Chapters.Single(item => item.Index == 2).Id);
            yield return null;
            Assert.That(Named("Departure.Start").interactable, Is.False);
            Click("Departure.Start", true); yield return null;
            Assert.That(app.CurrentScreenName, Is.EqualTo("Departure"));
            Assert.That(app.Progression.State.PendingRun, Is.Null);
            AssertWallet(state.Wallet, app.Progression.State.Wallet);
            Click("Departure.Chapter:" + catalog.Chapters.Single(item => item.Index == 1).Id); yield return null;
            Assert.That(Named("Departure.Start").interactable, Is.True);
            yield return CaptureSizes("departure", "Departure", false);
            yield return Reveal("Departure.CardPool"); Click("Departure.CardPool"); yield return ScreenIs("CardPool");
            app.NavigateBack(); yield return ScreenIs("Departure");
            app.EditorCaptureNotifyPause(true); app.EditorCaptureNotifyFocus(false); yield return null;
            app.EditorCaptureNotifyPause(false); app.EditorCaptureNotifyFocus(true); yield return null;
            Assert.That(app.CurrentScreenName, Is.EqualTo("Departure"));
            Assert.That(app.Progression.State.PendingRun, Is.Null);
            AssertWallet(state.Wallet, app.Progression.State.Wallet);
            app.NavigateBack(); yield return ScreenIs("Estate");
            yield return Reboot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            Assert.That(app.Progression.State.CompletedRuns, Is.EqualTo(completedRuns), "Reload must not settle a completed receipt twice.");
            Assert.That(app.Progression.State.PendingRun, Is.Null);
            Click("Estate.Depart"); yield return ScreenIs("Departure");
            Click("Departure.Start"); yield return ScreenIs("Run");
            Assert.That(app.Progression.State.PendingRun, Is.Not.Null);
            Assert.That(app.Progression.State.CompletedRuns, Is.EqualTo(completedRuns));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ValidSpendFixtureCancelsWithoutCostAndConfirmsOnlyOnce()
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var fixture = MetaEngine.NewGame(catalog);
            foreach (var material in catalog.Materials) fixture.Wallet[material.Id] = material.WalletCap;
            foreach (var building in catalog.ManorBuildings) fixture.ManorLevels[building.Id] = building.MaxLevel;
            SaveFixture(catalog, fixture); ReturningPreference();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            Click("Tab.Vassals"); yield return ScreenIs("Vassals");
            var id = catalog.Vassals.First(item => item.InitiallyUnlocked).Id;
            Click("Vassals.Select:" + id); yield return null;
            yield return Reveal("Vassals.Upgrade"); Click("Vassals.Upgrade"); yield return ScreenIs("SpendConfirmation");
            var before = app.Progression.State;
            app.EditorCaptureNotifyPause(true); yield return null; app.EditorCaptureNotifyPause(false); yield return null;
            Assert.That(app.CurrentScreenName, Is.EqualTo("SpendConfirmation"));
            Click("Spend.Cancel"); yield return ScreenIs("Vassals");
            Assert.That(MetaSaveCodec.Encode(app.Progression.State), Is.EqualTo(MetaSaveCodec.Encode(before)));
            yield return Reveal("Vassals.Upgrade"); Click("Vassals.Upgrade"); yield return ScreenIs("SpendConfirmation");
            app.NavigateBack(); yield return ScreenIs("Vassals");
            Assert.That(MetaSaveCodec.Encode(app.Progression.State), Is.EqualTo(MetaSaveCodec.Encode(before)));
            yield return Reveal("Vassals.Upgrade"); Click("Vassals.Upgrade"); yield return ScreenIs("SpendConfirmation");
            Click("Spend.Confirm"); yield return ScreenIs("Vassals");
            var expected = MetaEngine.UpgradeVassal(catalog, before, id, MetaRewardDelivery.PendingClaims);
            AssertWallet(expected.Wallet, app.Progression.State.Wallet);
            Assert.That(app.Progression.State.Vassals[id].Level, Is.EqualTo(before.Vassals[id].Level + 1));
            yield return Reboot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            AssertWallet(expected.Wallet, app.Progression.State.Wallet);
            Assert.That(app.Progression.State.Vassals[id].Level, Is.EqualTo(expected.Vassals[id].Level));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ValidPendingChallengeFixturePaysOneActualClaimAcrossReload()
        {
            var catalog = CanonicalContent.CreateMetaCatalog(); var fixture = MetaEngine.NewGame(catalog);
            var challenge = catalog.Challenges.First(item => item.ResearchLevel == 0 && item.Metric == MetaMetric.Runs);
            fixture.Metrics[challenge.Metric.ToString()] = challenge.Target;
            fixture = fixture with { NextClaimSequence = 2, PendingClaims = new[] { new MetaPendingClaim(1, MetaClaimKind.Challenge, challenge.Id, new Dictionary<string, int>(challenge.Rewards), 0) } };
            SaveFixture(catalog, fixture); ReturningPreference();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            Click("Tab.Challenges"); yield return ScreenIs("Challenges");
            var pending = app.Progression.State.PendingClaims.Single(item => item.SourceId == challenge.Id);
            var expected = MetaEngine.Claim(catalog, app.Progression.State, pending.Sequence);
            yield return Reveal("Challenges.Claim:" + challenge.Id);
            Click("Challenges.Claim:" + challenge.Id); yield return null;
            Assert.That(app.Progression.State.CompletedChallenges.Count(item => item == challenge.Id), Is.EqualTo(1));
            AssertWallet(expected.State.Wallet, app.Progression.State.Wallet);
            Assert.That(Named("Challenges.Claim:" + challenge.Id).interactable, Is.False);
            Click("Challenges.Claim:" + challenge.Id, true); yield return null;
            AssertWallet(expected.State.Wallet, app.Progression.State.Wallet);
            Assert.That(app.Progression.Claim(pending.Sequence).Claimed, Is.False, "The real save host must reject a duplicate receipt too.");
            yield return Reboot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            Click("Tab.Challenges"); yield return ScreenIs("Challenges");
            Assert.That(app.Progression.State.PendingClaims.Any(item => item.Sequence == pending.Sequence), Is.False);
            Assert.That(app.Progression.State.CompletedChallenges.Count(item => item == challenge.Id), Is.EqualTo(1));
            AssertWallet(expected.State.Wallet, app.Progression.State.Wallet);
            Assert.That(Named("Challenges.Claim:" + challenge.Id).interactable, Is.False);
            yield return CaptureSizes("claimed-fixture", "Challenges", true);
        }

        void ReturningPreference() { var preferences = RunPreferences.Load(prefix); preferences.IntroductionSeen = true; preferences.Save(); }
        void SaveFixture(MetaCatalog catalog, MetaState state)
        {
            Assert.That(MetaValidation.ValidateState(catalog, state), Is.Empty);
            new AtomicSaveStore(directory, "progress.bin").Save(MetaSaveCodec.Encode(state), bytes =>
            {
                var decoded = MetaSaveCodec.Decode(bytes);
                return decoded.Valid && MetaValidation.ValidateState(catalog, decoded.State).Length == 0 ? AtomicSaveValidation.Valid : AtomicSaveValidation.Corrupt;
            });
        }
        IEnumerator Reboot()
        {
            foreach (var boot in Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None)) Object.Destroy(boot.gameObject);
            yield return null; yield return Boot();
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
        void Click(string name, bool allowDisabled = false)
        {
            Canvas.ForceUpdateCanvases(); var button = Named(name); if (!allowDisabled) Assert.That(button.interactable, Is.True, name);
            var rect = (RectTransform)button.transform; var point = Pixels(rect).center;
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
        IEnumerator CaptureSizes(string name, string expectedScreen, bool fixture)
        {
            foreach (var size in new[] { new Vector2Int(390, 844), new Vector2Int(1080, 2520) })
            {
                PlayModeWindow.SetCustomRenderingResolution((uint)size.x, (uint)size.y, "Portrait meta QA");
                yield return null; yield return null; app.Ui.SetPortraitMobileSimulation(true); app.Ui.SynchronizeViewport();
                yield return new WaitForSecondsRealtime(.2f); Canvas.ForceUpdateCanvases();
                Assert.That(Screen.width, Is.EqualTo(size.x)); Assert.That(Screen.height, Is.EqualTo(size.y));
                Assert.That(app.CurrentScreenName, Is.EqualTo(expectedScreen));
                var scroll = app.Ui.GetComponentInChildren<ScrollRect>();
                foreach (var position in new[] { 1f, 0f })
                {
                    if (scroll != null) scroll.verticalNormalizedPosition = position;
                    yield return null; yield return null; Canvas.ForceUpdateCanvases(); AssertGeometry();
                    var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-234/editor-meta")); Directory.CreateDirectory(output);
                    var path = Path.Combine(output, name + "-" + size.x + "x" + size.y + (position == 0 ? "-bottom" : "") + ".png");
                    if (File.Exists(path)) File.Delete(path); ScreenCapture.CaptureScreenshot(path); var deadline = Time.realtimeSinceStartup + 10;
                    while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(File.Exists(path), Is.True, path);
                    var image = new Texture2D(2, 2);
                    try { Assert.That(image.LoadImage(File.ReadAllBytes(path)), Is.True); Assert.That(image.width, Is.EqualTo(size.x)); Assert.That(image.height, Is.EqualTo(size.y)); }
                    finally { Object.Destroy(image); }
                    File.WriteAllText(path + ".txt", $"surface=actual Unity Editor PlayMode UI; mobileAspectSimulation=true; physicalDevice=false; userAcceptance=false; automatedPointerActions=true; accountFixture={fixture}; screen={app.CurrentScreenName}; commit={BuildIdentity.Commit}; sourceHash={BuildIdentity.SourceHash}; sourceDirty={BuildIdentity.SourceDirty}; dataHash={CanonicalContent.DataHash}; safePixels={app.Ui.SafePixels}; normalPlayEvidence=false\n");
                }
                if (scroll != null) scroll.verticalNormalizedPosition = 1;
                yield return null;
            }
        }
        void AssertGeometry()
        {
            var safe = app.Ui.SafePixels; var scale = app.Ui.Canvas.scaleFactor;
            var footer = new List<Rect>();
            foreach (var button in app.Ui.GetComponentsInChildren<Button>())
            {
                var bounds = Pixels((RectTransform)button.transform);
                Assert.That(bounds.width / scale, Is.GreaterThanOrEqualTo(43.99f), button.name);
                Assert.That(bounds.height / scale, Is.GreaterThanOrEqualTo(43.99f), button.name);
                var scroll = button.GetComponentInParent<ScrollRect>();
                if (scroll != null && scroll.viewport != null)
                {
                    var clip = Pixels(scroll.viewport); if (!clip.Overlaps(bounds)) continue;
                    bounds = Rect.MinMaxRect(Mathf.Max(bounds.xMin, clip.xMin), Mathf.Max(bounds.yMin, clip.yMin), Mathf.Min(bounds.xMax, clip.xMax), Mathf.Min(bounds.yMax, clip.yMax));
                }
                else if (button.name.StartsWith("Tab.", StringComparison.Ordinal) || button.name == "Estate.Depart" || button.name == "Departure.Start" || button.name.StartsWith("Summary.", StringComparison.Ordinal))
                {
                    footer.Add(bounds); Assert.That(bounds.yMax, Is.LessThanOrEqualTo(safe.yMin + safe.height / 3), button.name + " must remain in the lower thumb zone.");
                }
                Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(safe.xMin - 1)); Assert.That(bounds.xMax, Is.LessThanOrEqualTo(safe.xMax + 1));
                Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(safe.yMin - 1)); Assert.That(bounds.yMax, Is.LessThanOrEqualTo(safe.yMax + 1));
            }
            foreach (var scroll in app.Ui.GetComponentsInChildren<ScrollRect>())
                foreach (var bounds in footer) Assert.That(Pixels(scroll.viewport).yMin, Is.GreaterThanOrEqualTo(bounds.yMax - 1), "Scrolling content must not cover a fixed footer.");
        }
        static void AssertWallet(Dictionary<string, int> expected, Dictionary<string, int> actual)
            => Assert.That(actual.OrderBy(item => item.Key), Is.EqualTo(expected.OrderBy(item => item.Key)));
        static Rect Pixels(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners.Min(point => point.x), corners.Min(point => point.y), corners.Max(point => point.x), corners.Max(point => point.y));
        }
    }
}
#endif
