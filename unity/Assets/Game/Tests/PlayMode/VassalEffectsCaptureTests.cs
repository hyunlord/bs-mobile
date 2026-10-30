#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    public sealed class VassalEffectsCaptureTests
    {
        string directory, prefix, priorDirectory, priorPrefix;
        int priorWidth, priorHeight;
        RunCoordinator app;
        MetaCatalog catalog;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            if (CanonicalContent.ProfileName != "wave-1a")
            {
                Assert.Ignore("Vassal presentation fixtures require the wave-1a portrait UI.");
            }
            Assert.That(Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None), Is.Empty);
            priorDirectory = RunCoordinator.MetaDirectoryOverride;
            priorPrefix = RunCoordinator.PreferencePrefixOverride;
            priorWidth = Screen.width; priorHeight = Screen.height;
            var id = Guid.NewGuid().ToString("N");
            directory = Path.Combine(Path.GetTempPath(), "sowsiege-vassal-captures-" + id);
            prefix = "sowsiege.test.vassal-captures." + id + ".";
            RunCoordinator.MetaDirectoryOverride = directory;
            RunCoordinator.PreferencePrefixOverride = prefix;
            PlayModeWindow.SetCustomRenderingResolution(390, 844, "Vassal fixture QA");
            yield return SceneManager.LoadSceneAsync("Boot");
            var deadline = Time.realtimeSinceStartup + 45;
            while ((app = Object.FindFirstObjectByType<RunCoordinator>()) == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.That(app, Is.Not.Null); yield return null;
            Assert.That(app.Error, Is.Null);
            app.Ui.SetPortraitMobileSimulation(true); app.Ui.SynchronizeViewport();
            app.StartRun(411);
            deadline = Time.realtimeSinceStartup + 30;
            while ((app.Frame == null || app.CurrentScreenName != "Run") && app.Error == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.That(app.Error, Is.Null); Assert.That(app.Frame, Is.Not.Null);
            Assert.That(app.CurrentScreenName, Is.EqualTo("Run"));
            yield return null; yield return null;
            app.enabled = false;
            catalog = CanonicalContent.CreateMetaCatalog();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var boot in Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None))
            {
                Object.Destroy(boot.gameObject);
            }
            yield return null;
            if (directory != null)
            {
                RunCoordinator.MetaDirectoryOverride = priorDirectory;
                RunCoordinator.PreferencePrefixOverride = priorPrefix;
                foreach (var key in RunPreferences.Keys) { PlayerPrefs.DeleteKey(prefix + key); }
                PlayerPrefs.Save();
                if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
                PlayModeWindow.SetCustomRenderingResolution((uint)priorWidth, (uint)priorHeight, "Restored test viewport");
            }
            app = null;
            yield return null;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator RenderOffEmptyCappedDepartureAndEquipmentStressFixtures()
        {
            var empty = MaximumState();
            ShowList(empty, null);
            Assert.That(Texts(), Does.Contain("이번 출정에서는 가신 능력 보너스가 적용되지 않습니다."));
            yield return Capture("fixture-vassals-off", "opt-in absent; maximum owned state; empty roster");
            var offScroll = app.Ui.GetComponentInChildren<ScrollRect>();
            if (offScroll != null)
            {
                offScroll.verticalNormalizedPosition = 0;
                yield return Capture("fixture-vassals-off-bottom", "opt-in absent; scroll bottom exposes historical off notice");
            }

            var enabledEmpty = VassalRun.Create(catalog, empty, CanonicalContent.CreateVassalRunRules());
            ShowList(empty, enabledEmpty);
            Assert.That(Texts(), Does.Contain("편성 0 / " + enabledEmpty.Snapshot.EffectiveSlots + " · 판 안 능력"));
            yield return Capture("fixture-vassals-enabled-empty", "opt-in present; maximum owned state; empty roster");

            var maximum = MaximumState(MetaAbility.Attack, MetaAbility.Health, MetaAbility.Experience);
            var capped = VassalRun.Create(catalog, maximum, CanonicalContent.CreateVassalRunRules());
            ShowList(maximum, capped);
            Assert.That(Texts(), Does.Contain("상한 도달 — 더 강화해도 전투 효과 없음"));
            yield return Capture("fixture-vassals-capped", "maximum owned state; attack/health/experience selected");

            PortraitMetaPanels.VassalDepartureConfirmation(app.Ui, catalog, capped, () => { }, () => { }, () => { });
            Assert.That(Texts(), Does.Contain("편성 " + capped.Snapshot.Roster.Count + " / " + capped.Snapshot.EffectiveSlots));
            yield return Capture("fixture-vassal-departure-capped", "frozen capped departure over list backdrop; not account flow");

            foreach (var size in new[] { 2, 7 })
            {
                app.Ui.Clear(); yield return null;
                var content = FoundationBoot.Catalog;
                var weapons = new[] { "core:iron_blade", "core:harvest_scythe" }
                    .Concat(content.Weapons.Keys.OrderBy(id => id, StringComparer.Ordinal)).Where(content.Weapons.ContainsKey)
                    .Distinct(StringComparer.Ordinal).Take(size).ToArray();
                var tools = new[] { "core:seed_bag", "core:rain_ladle" }
                    .Concat(content.Tools.Keys.OrderBy(id => id, StringComparer.Ordinal)).Where(content.Tools.ContainsKey)
                    .Distinct(StringComparer.Ordinal).Take(size).ToArray();
                Assert.That(weapons.Length, Is.EqualTo(size)); Assert.That(tools.Length, Is.EqualTo(size));
                var equipment = weapons.Concat(tools).Select(id => new EquipmentView(id, size == 2 ? 1 : 12)).ToArray();
                var hud = new PortraitHud(app.Ui, content, () => { }, false, capped);
                hud.Present(app.Frame with { Equipment = equipment }, app.FirstPlayable);
                Canvas.ForceUpdateCanvases();
                Assert.That(hud.ReservedTopPixels, Is.LessThanOrEqualTo(84.4f));
                var pause = app.Ui.GetComponentsInChildren<Button>().Single(button => button.name == "멈춤");
                var pauseBounds = Pixels((RectTransform)pause.transform);
                Assert.That(pauseBounds.width, Is.GreaterThanOrEqualTo(48));
                Assert.That(pauseBounds.height, Is.GreaterThanOrEqualTo(48));
                Assert.That(app.Ui.GetComponentsInChildren<Transform>().Count(item => item.name.StartsWith("Equipment ", StringComparison.Ordinal) && item.name != "Equipment pool"), Is.EqualTo(4));
                Assert.That(app.Ui.GetComponentsInChildren<Transform>().Count(item => item.name.StartsWith("Folded equipment ", StringComparison.Ordinal)), Is.EqualTo(size == 2 ? 0 : 2));
                yield return Capture(size == 2 ? "fixture-vassal-hud-early4" : "fixture-vassal-hud-late14",
                    "render-only equipment stress fixture; distinct weapons=" + size + "; distinct tools=" + size + "; no loadout cap change");
            }
        }

        void ShowList(MetaState state, VassalRunOptions options)
        {
            app.Ui.Clear();
            PortraitMetaPanels.Vassals(app.Ui, catalog, state, catalog.Vassals[0].Id,
                _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, options);
        }

        MetaState MaximumState(params MetaAbility[] abilities)
        {
            var state = MetaEngine.NewGame(catalog);
            foreach (var building in catalog.ManorBuildings) { state.ManorLevels[building.Id] = building.MaxLevel; }
            state = state with
            {
                CompletedChallenges = catalog.Challenges.Select(item => item.Id).ToArray(),
                UnlockedContentIds = catalog.InitialContentIds.Concat(catalog.Challenges.SelectMany(item => item.UnlockContentIds)).Distinct(StringComparer.Ordinal).ToArray(),
                ActiveVassalIds = abilities.Select(ability => catalog.Vassals.Single(item => item.Ability == ability).Id).ToArray()
            };
            foreach (var definition in catalog.Vassals)
            {
                state.Vassals[definition.Id] = new(definition.MaxLevel, definition.MaxRank, 0,
                    definition.InitiallyUnlocked || catalog.Challenges.Any(item => item.UnlockVassalIds.Contains(definition.Id)));
            }
            return state;
        }

        IEnumerator Capture(string name, string fixture)
        {
            yield return null; yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(Screen.width, Is.EqualTo(390)); Assert.That(Screen.height, Is.EqualTo(844));
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/vassal-539/fixture-captures"));
            Directory.CreateDirectory(output);
            var path = Path.Combine(output, name + "-390x844.png");
            yield return PlayModeCaptureGuard.WaitForShaders(path);
            var probe = PlayModeCaptureGuard.Begin(path);
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Assert.That(image, Is.Not.Null);
                Assert.That(image.width, Is.EqualTo(390)); Assert.That(image.height, Is.EqualTo(844));
                PlayModeCaptureGuard.Check(image, probe, path);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { if (image != null) { Object.Destroy(image); } }
            File.WriteAllText(path + ".txt", $"surface=actual Unity Editor PlayMode UI; testFixture=true; normalPlayEvidence=false; physicalDevice=false; userAcceptance=false; fixture={fixture}; commit={BuildIdentity.Commit}; sourceHash={BuildIdentity.SourceHash}; sourceDirty={BuildIdentity.SourceDirty}; dataHash={CanonicalContent.DataHash}\n");
        }

        string[] Texts() => app.Ui.GetComponentsInChildren<Text>().Select(text => text.text).ToArray();
        static Rect Pixels(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners.Min(point => point.x), corners.Min(point => point.y), corners.Max(point => point.x), corners.Max(point => point.y));
        }
    }
}
#endif
