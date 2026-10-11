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
    public sealed class PortraitBmFlowTests
    {
        string directory, prefix, priorDirectory, priorPrefix;
        int priorWidth, priorHeight;
        RunCoordinator app;
        Func<RewardedAdsPolicy> oldPolicyFactory;
        IRewardedAdProvider oldProvider;
        HostRewardRunOptions oldHostOptions;
        MetaBonusPolicy oldResultBonus;
        bool oldRemoval;
        int? oldCompletedRuns;
        int day;
        RewardedAdsPolicy policy;

        [UnitySetUp] public IEnumerator Setup()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            Assert.That(Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None), Is.Empty);
            oldPolicyFactory = RunCoordinator.EditorRewardPolicyFactory; oldProvider = RunCoordinator.EditorRewardProvider;
            oldHostOptions = RunCoordinator.EditorHostRewardOptions; oldResultBonus = RunCoordinator.EditorResultBonus;
            oldRemoval = RunCoordinator.EditorAdRemovalOwned; oldCompletedRuns = RunCoordinator.EditorCompletedRuns;
            RunCoordinator.EditorRewardPolicyFactory = null; RunCoordinator.EditorRewardProvider = null;
            RunCoordinator.EditorHostRewardOptions = null; RunCoordinator.EditorResultBonus = null;
            RunCoordinator.EditorAdRemovalOwned = false; RunCoordinator.EditorCompletedRuns = null;
            priorDirectory = RunCoordinator.MetaDirectoryOverride; priorPrefix = RunCoordinator.PreferencePrefixOverride;
            priorWidth = Screen.width; priorHeight = Screen.height;
            var id = Guid.NewGuid().ToString("N");
            directory = Path.Combine(Path.GetTempPath(), "sowsiege-bm-flow-" + id);
            prefix = "sowsiege.test.bm-flow." + id + ".";
            RunCoordinator.MetaDirectoryOverride = directory; RunCoordinator.PreferencePrefixOverride = prefix;
            PlayModeWindow.SetCustomRenderingResolution(390, 844, "Portrait BM flow QA");
            yield return null;
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var boot in Object.FindObjectsByType<FoundationBoot>(FindObjectsSortMode.None)) Object.Destroy(boot.gameObject);
            yield return null;
            if (directory != null)
            {
                RunCoordinator.EditorRewardPolicyFactory = oldPolicyFactory; RunCoordinator.EditorRewardProvider = oldProvider;
                RunCoordinator.EditorHostRewardOptions = oldHostOptions; RunCoordinator.EditorResultBonus = oldResultBonus;
                RunCoordinator.EditorAdRemovalOwned = oldRemoval; RunCoordinator.EditorCompletedRuns = oldCompletedRuns;
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
        public IEnumerator ShopPassIdleAndFakeSettingsUseRealRoutesWithoutSpending()
        {
            PrepareAccount(); yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            var wallet = app.Progression.State.Wallet;
            Click("Tab.Shop"); yield return ScreenIs("Shop");
            Assert.That(app.Ui.GetComponentsInChildren<Text>().Any(label => label.text.Contains("보기 전용")), Is.True);
            yield return CaptureSizes("shop", "Shop", true);
            yield return Reveal("Shop.Pass"); Click("Shop.Pass"); yield return ScreenIs("Pass");
            Assert.That(app.Ui.GetComponentsInChildren<Text>().Any(label => label.text.Contains("판 안 힘은 없음")), Is.True);
            yield return CaptureSizes("pass", "Pass", true);
            yield return BackgroundAndReturn("Pass");
            app.NavigateBack(); yield return ScreenIs("Shop");
            yield return Reveal("Shop.AdRemoval"); Click("Shop.AdRemoval"); yield return ScreenIs("AdRemoval");
            AssertWallet(wallet, app.Progression.State.Wallet);
            app.NavigateBack(); yield return ScreenIs("Shop"); app.NavigateBack(); yield return ScreenIs("Estate");
            yield return Reveal("Estate.Idle"); Click("Estate.Idle"); yield return ScreenIs("Idle");
            Assert.That(Named("Idle.Claim").interactable, Is.True);
            Assert.That(Buttons("Idle.OptionalAd").Length, Is.EqualTo(1));
            yield return CaptureSizes("idle", "Idle", true);
            yield return BackgroundAndReturn("Idle"); app.NavigateBack(); yield return ScreenIs("Estate");
            Click("Estate.Settings"); yield return ScreenIs("Settings");
            yield return Reveal("Settings.FakeAds"); Click("Settings.FakeAds"); yield return ScreenIs("FakeAdSettings");
            Click("FakeAd.Failure"); yield return ScreenIs("FakeAdSettings");
            Assert.That(Named("FakeAd.Failure").GetComponentInChildren<Text>().text, Does.Contain("현재 선택"));
            yield return CaptureSizes("fake-settings", "FakeAdSettings", true);
            Click("FakeAd.Success"); yield return ScreenIs("FakeAdSettings");
            yield return BackgroundAndReturn("FakeAdSettings");
            app.NavigateBack(); yield return ScreenIs("Settings"); app.NavigateBack(); yield return ScreenIs("Estate");
            AssertWallet(wallet, app.Progression.State.Wallet);
            Assert.That(app.Progression.State.RewardReceipts, Is.Empty);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator UnavailableProviderHidesAdAndOrdinaryIdleClaimSurvivesReloadOnce()
        {
            PrepareAccount(); RunCoordinator.EditorRewardProvider = new RewardedAdsUnavailableProvider();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            yield return Reveal("Estate.Idle"); Click("Estate.Idle"); yield return ScreenIs("Idle");
            Assert.That(Buttons("Idle.OptionalAd"), Is.Empty);
            yield return CaptureSizes("idle-unavailable", "Idle", true);
            var before = app.Progression.State; var claim = before.PendingClaims.Single(value => value.Kind == MetaClaimKind.Idle);
            var expected = MetaEngine.Claim(app.Progression.Catalog, before, claim.Sequence).State;
            Click("Idle.Claim"); yield return ScreenIs("Idle");
            AssertWallet(expected.Wallet, app.Progression.State.Wallet);
            Assert.That(Named("Idle.Claim").interactable, Is.False);
            yield return Reboot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            yield return Reveal("Estate.Idle"); Click("Estate.Idle"); yield return ScreenIs("Idle");
            Assert.That(Buttons("Idle.OptionalAd"), Is.Empty);
            Assert.That(app.Progression.Claim(claim.Sequence).Claimed, Is.False);
            AssertWallet(expected.Wallet, app.Progression.State.Wallet);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator FakeFailureConsumesNeitherIdleClaimNorQuotaAndScreenVisitsDoNotOfferAgain()
        {
            PrepareAccount(); yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            Click("Estate.Settings"); yield return ScreenIs("Settings");
            yield return Reveal("Settings.FakeAds"); Click("Settings.FakeAds"); yield return ScreenIs("FakeAdSettings");
            Click("FakeAd.Failure"); yield return ScreenIs("FakeAdSettings");
            Click("FakeAd.Back"); yield return ScreenIs("Settings"); app.NavigateBack(); yield return ScreenIs("Estate");
            yield return Reveal("Estate.Idle"); Click("Estate.Idle"); yield return ScreenIs("Idle");
            var before = app.Progression.State; var sequence = before.PendingClaims.Single(value => value.Kind == MetaClaimKind.Idle).Sequence;
            Click("Idle.OptionalAd"); yield return ScreenIs("Idle");
            AssertWallet(before.Wallet, app.Progression.State.Wallet);
            Assert.That(app.Progression.State.PendingClaims.Any(value => value.Sequence == sequence), Is.True);
            Assert.That(policy.TodayIdleCompletions, Is.Zero);
            Assert.That(app.Progression.State.RewardReceipts, Is.Empty);
            Assert.That(Buttons("Idle.OptionalAd"), Is.Empty);
            app.NavigateBack(); yield return ScreenIs("Estate"); yield return Reveal("Estate.Idle"); Click("Estate.Idle"); yield return ScreenIs("Idle");
            Assert.That(Buttons("Idle.OptionalAd"), Is.Empty);
            Assert.That(Named("Idle.Claim").interactable, Is.True);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator RemovalEntitlementDeliversIdleBonusOnceWithUnavailableProviderAndPersistsReceipt()
        {
            PrepareAccount(); RunCoordinator.EditorAdRemovalOwned = true;
            RunCoordinator.EditorRewardProvider = new RewardedAdsUnavailableProvider();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            yield return Reveal("Estate.Idle"); Click("Estate.Idle"); yield return ScreenIs("Idle");
            Assert.That(Named("Idle.OptionalAd").GetComponentInChildren<Text>().text, Does.StartWith("바로 2배"));
            var before = app.Progression.State; var claim = before.PendingClaims.Single(value => value.Kind == MetaClaimKind.Idle);
            var cap = app.Progression.Catalog.Materials.ToDictionary(value => value.Id, value => value.WalletCap);
            var expected = MetaEngine.ClaimIdleWithBonus(app.Progression.Catalog, before, claim.Sequence, "expected-fixture-only", new MetaBonusPolicy(1000, cap)).State;
            Click("Idle.OptionalAd"); yield return ScreenIs("Idle");
            AssertWallet(expected.Wallet, app.Progression.State.Wallet);
            var receipt = app.Progression.State.RewardReceipts.Single(value => value.Kind == MetaBonusKind.Idle);
            Assert.That(receipt.SourceSequence, Is.EqualTo(claim.Sequence));
            Assert.That(policy.TodayIdleCompletions, Is.EqualTo(1)); Assert.That(policy.PendingRewards, Is.Empty);
            Assert.That(Buttons("Idle.OptionalAd"), Is.Empty);
            yield return Reboot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            AssertWallet(expected.Wallet, app.Progression.State.Wallet);
            Assert.That(app.Progression.State.RewardReceipts.Count(value => value.ReceiptId == receipt.ReceiptId), Is.EqualTo(1));
            Assert.That(app.Progression.ClaimIdleWithBonus(claim.Sequence, receipt.ReceiptId, new MetaBonusPolicy(1000, cap)).Granted, Is.False);
            AssertWallet(expected.Wallet, app.Progression.State.Wallet);
            Assert.That(policy.TodayIdleCompletions, Is.EqualTo(1));
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator RecordedDeathFixtureRevivesThroughActualButtonAndReplaysFinalHash()
        {
            PrepareAccount(); RunCoordinator.EditorAdRemovalOwned = true;
            RunCoordinator.EditorHostRewardOptions = new HostRewardRunOptions(true, new HostReviveRules(100, 30, HostRevivePreservation.PreserveWorldAndCards));
            var preferences = RunPreferences.Load(prefix); preferences.FeelHealth = true; preferences.FeelContact = true; preferences.Save();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            Click("Estate.Depart"); yield return ScreenIs("Departure"); Click("Departure.Start"); yield return ScreenIs("Run");
            for (var batch = 0; batch < 120 && app.CurrentScreenName != "Revive" && app.CurrentScreenName != "Summary"; batch++)
            {
                app.EditorAdvanceRewardFixture(256); Assert.That(app.Error, Is.Null); yield return null;
            }
            yield return ScreenIs("Revive");
            Assert.That(app.Session.CanHostRevive, Is.True);
            Assert.That(app.Progression.State.PendingRun, Is.Not.Null, "Death must not settle before the revival choice.");
            var resultBounds = Pixels((RectTransform)Named("Revive.Result").transform);
            var adBounds = Pixels((RectTransform)Named("Revive.OptionalAd").transform);
            Assert.That(adBounds.size, Is.EqualTo(resultBounds.size));
            yield return CaptureSizes("revive-rules-fixture", "Revive", true);
            yield return BackgroundAndReturn("Revive");
            Click("Revive.OptionalAd"); yield return null;
            Assert.That(app.Error, Is.Null); Assert.That(app.Session.HostRevived, Is.True);
            app.OpenPause(); yield return ScreenIs("Pause");
            Click("Pause.Abandon"); yield return ScreenIs("ConfirmAbandon"); Click("Abandon.Confirm"); yield return ScreenIs("Summary");
            var replayPath = app.RecordedReplayPath;
            using (var input = File.OpenRead(replayPath))
            {
                var replay = ReplayCodec.Read(input);
                Assert.That(replay.Commands.Count(command => command.Kind == ReplayCommandKind.HostReviveV1), Is.EqualTo(1));
                Assert.That(replay.Commands.Any(command => command.Kind == ReplayCommandKind.SetInvulnerable || command.Kind == ReplayCommandKind.SetSpawnPermille || command.Kind == ReplayCommandKind.GrantLevel), Is.False);
                Assert.That(ReplayRunner.Verify(FoundationBoot.Catalog, FoundationBoot.VerifiedDataHash, replay).StateHash, Is.EqualTo(replay.End.StateHash));
            }
            Assert.That(app.Session.GetSummary().Revived, Is.True);
            var evidence = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-234/editor-bm"));
            Directory.CreateDirectory(evidence);
            File.Copy(replayPath, Path.Combine(evidence, "revive-fixture.ssreplay"), true);
            File.WriteAllText(Path.Combine(evidence, "revive-fixture.txt"), "Preselected Editor fixture: idle normal Advance/legal card commands; health/contact experiment settings; explicit revive health100/invulnerability30; actual UI reward; final replay hash verified. Not natural-play or production revive-balance acceptance.\n");
        }

        sealed class OutcomeProvider : IRewardedAdProvider
        {
            public bool IsAvailable => true;
            public RewardedAdOutcome Outcome;
            public int Calls;
            public RewardedAdOutcome Show(RewardedAdSlot slot, string completionId) { Calls++; return Outcome; }
        }
        sealed class FailingRewardStore : IRewardedAdsStore
        {
            readonly IRewardedAdsStore inner;
            public bool Fail;
            public int Writes, FailFrom = -1;
            public FailingRewardStore(string path) { inner = new RewardedAdsAtomicStore(path); }
            public byte[] Load() => inner.Load();
            public void Save(byte[] bytes) { Writes++; if(Fail || FailFrom >= 0 && Writes >= FailFrom) throw new IOException("Fixture reward persistence failure."); inner.Save(bytes); }
        }
        IEnumerator ReachRevive()
        {
            RunCoordinator.EditorHostRewardOptions = new HostRewardRunOptions(true, new HostReviveRules(100, 30, HostRevivePreservation.PreserveWorldAndCards));
            var preferences = RunPreferences.Load(prefix); preferences.FeelHealth = true; preferences.FeelContact = true; preferences.Save();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            Click("Estate.Depart"); yield return ScreenIs("Departure"); Click("Departure.Start"); yield return ScreenIs("Run");
            for(var batch = 0; batch < 120 && app.CurrentScreenName != "Revive" && app.CurrentScreenName != "Summary"; batch++)
            { app.EditorAdvanceRewardFixture(256); Assert.That(app.Error, Is.Null); yield return null; }
            yield return ScreenIs("Revive");
        }
        [UnityTest, Timeout(300000)]
        public IEnumerator FailedReviveAdvertisementKeepsChoiceUntilExplicitResult()
        { yield return UnsuccessfulRevive(RewardedAdOutcome.Failed); }
        [UnityTest, Timeout(300000)]
        public IEnumerator CancelledReviveAdvertisementKeepsChoiceUntilExplicitResult()
        { yield return UnsuccessfulRevive(RewardedAdOutcome.Cancelled); }
        IEnumerator UnsuccessfulRevive(RewardedAdOutcome outcome)
        {
            PrepareAccount();
            var provider = new OutcomeProvider { Outcome = outcome };
            RunCoordinator.EditorRewardProvider = provider;
            yield return ReachRevive(); var pending = app.Progression.State.PendingRun;
            Click("Revive.OptionalAd"); yield return ScreenIs("Revive");
            Assert.That(app.Progression.State.PendingRun, Is.EqualTo(pending));
            Assert.That(app.Session.HostRevived, Is.False); Assert.That(provider.Calls, Is.EqualTo(1));
            Assert.That(Buttons("Revive.OptionalAd"), Is.Empty); Assert.That(Buttons("Revive.RetrySave"), Is.Empty);
            yield return BackgroundAndReturn("Revive");
            Click("Revive.Result"); yield return ScreenIs("Summary");
            Assert.That(app.Progression.State.PendingRun, Is.Null);
        }
        [UnityTest, Timeout(300000)]
        public IEnumerator PendingReviveReceiptRetriesStorageWithoutAnotherAdvertisement()
        {
            PrepareAccount();
            var store = new FailingRewardStore(Path.Combine(directory, "retry-policy"));
            day = 100; policy = new RewardedAdsPolicy(store, () => day); day = 101;
            RunCoordinator.EditorRewardPolicyFactory = () => policy = new RewardedAdsPolicy(store, () => day);
            var provider = new OutcomeProvider { Outcome = RewardedAdOutcome.Completed };
            RunCoordinator.EditorRewardProvider = provider;
            yield return ReachRevive(); var pending = app.Progression.State.PendingRun;
            store.Fail = true; Click("Revive.OptionalAd"); yield return ScreenIs("Revive");
            Assert.That(policy.HasPendingWrite, Is.True); Assert.That(app.Progression.State.PendingRun, Is.EqualTo(pending));
            Assert.That(app.Session.HostRevived, Is.False); Assert.That(Buttons("Revive.OptionalAd"), Is.Empty);
            Assert.That(Named("Revive.RetrySave").interactable, Is.True);
            yield return CaptureSizes("revive-pending-save", "Revive", false);
            store.Fail = false; Click("Revive.RetrySave"); yield return null;
            Assert.That(app.Error, Is.Null); Assert.That(app.Session.HostRevived, Is.True);
            Assert.That(policy.HasPendingWrite, Is.False); Assert.That(policy.PendingRewards, Is.Empty);
            Assert.That(provider.Calls, Is.EqualTo(1)); Assert.That(app.Progression.State.PendingRun, Is.EqualTo(pending));
            app.OpenPause(); yield return ScreenIs("Pause"); Click("Pause.Abandon"); yield return ScreenIs("ConfirmAbandon");
            Click("Abandon.Confirm"); yield return ScreenIs("Summary");
            using(var input = File.OpenRead(app.RecordedReplayPath))
            {
                var replay = ReplayCodec.Read(input);
                Assert.That(replay.Commands.Count(command => command.Kind == ReplayCommandKind.HostReviveV1), Is.EqualTo(1));
                Assert.That(ReplayRunner.Verify(FoundationBoot.Catalog, FoundationBoot.VerifiedDataHash, replay).StateHash, Is.EqualTo(replay.End.StateHash));
            }
        }
        [UnityTest, Timeout(120000)]
        public IEnumerator ResultBonusDisplaysWalletOverflowAndManorGrowthExactlyOnce()
        {
            PrepareAccount(); var catalog = CanonicalContent.CreateMetaCatalog();
            var store = new FailingRewardStore(Path.Combine(directory, "result-retry-policy"));
            day = 100; policy = new RewardedAdsPolicy(store, () => day); day = 101;
            RunCoordinator.EditorRewardPolicyFactory = () => policy = new RewardedAdsPolicy(store, () => day);
            var building = catalog.ManorBuildings.First(b => b.BaseCost.Count(pair => pair.Value > 0) > 1);
            var fixture = MetaEngine.NewGame(catalog) with { CompletedRuns = 2, NextRunSequence = 3 };
            foreach(var other in catalog.ManorBuildings) fixture.ManorLevels[other.Id] = other.MaxLevel;
            fixture.ManorLevels[building.Id] = building.MaxLevel - 1;
            foreach(var material in catalog.Materials)
            {
                building.BaseCost.TryGetValue(material.Id, out var basis); building.CostPerLevel.TryGetValue(material.Id, out var step);
                fixture.Wallet[material.Id] = Math.Max(0, basis + step * (building.MaxLevel - 1) - 1);
            }
            var capped = catalog.Materials[0]; fixture.Wallet[capped.Id] = capped.WalletCap;
            var awarded = catalog.Materials.ToDictionary(m => m.Id, m => 2);
            fixture = fixture with { ResultRewardBasis = new MetaResultRewardBasis(2, awarded) };
            SaveFixture(catalog, fixture);
            var caps = catalog.Materials.ToDictionary(m => m.Id, m => m.WalletCap);
            RunCoordinator.EditorResultBonus = new MetaBonusPolicy(1000, caps);
            RunCoordinator.EditorAdRemovalOwned = true;
            yield return Boot(); app.StartNeutralRun(42); yield return ScreenIs("Run");
            app.OpenPause(); yield return ScreenIs("Pause"); Click("Pause.Abandon"); yield return ScreenIs("ConfirmAbandon");
            Click("Abandon.Confirm"); yield return ScreenIs("Summary");
            var before = app.Progression.State;
            var expected = MetaEngine.GrantResultBonus(catalog, before, 2, "expected-result-ui", RunCoordinator.EditorResultBonus);
            Assert.That(expected.Overflow[capped.Id], Is.GreaterThan(0));
            Assert.That(expected.BuildingsGrown, Does.Contain(building.Id));
            var existingOverflow = catalog.Materials.ToDictionary(m => m.Id, m => 7);
            app.EditorPresentSettledRewardFixture(new MetaSettlement(before, false, awarded, existingOverflow, new[] { building.Id }, Array.Empty<string>()));
            yield return ScreenIs("Summary");
            store.FailFrom = store.Writes + 2; // Receipt saves; its acknowledgement fails after the atomic meta reward.
            Click("Summary.OptionalAd"); yield return ScreenIs("Summary");
            Assert.That(policy.HasPendingWrite, Is.True);
            var actual = app.EditorRewardSettlement;
            AssertWallet(expected.State.Wallet, app.Progression.State.Wallet);
            Assert.That(actual.Overflow[capped.Id], Is.EqualTo(7 + expected.Overflow[capped.Id]));
            Assert.That(actual.BuildingsGrown.Count(id => id == building.Id), Is.EqualTo(1));
            Assert.That(app.Ui.GetComponentsInChildren<Text>().Any(t => t.text.Contains("보관 초과 " + actual.Overflow[capped.Id])), Is.True);
            Assert.That(app.Ui.GetComponentsInChildren<Text>().Count(t => t.text == building.Name + " · 성장"), Is.EqualTo(1));
            Assert.That(Buttons("Summary.OptionalAd"), Is.Empty);
            app.EditorRetryRewardDelivery(); yield return ScreenIs("Summary");
            Assert.That(app.EditorRewardSettlement.Overflow[capped.Id], Is.EqualTo(actual.Overflow[capped.Id]));
            Assert.That(app.EditorRewardSettlement.BuildingsGrown, Is.EqualTo(actual.BuildingsGrown));
            Assert.That(app.Progression.State.RewardReceipts.Count(r => r.Kind == MetaBonusKind.Result), Is.EqualTo(1));
            store.FailFrom = -1; app.EditorRetryRewardDelivery(); yield return ScreenIs("Summary");
            Assert.That(policy.PendingRewards, Is.Empty); Assert.That(policy.HasPendingWrite, Is.False);
            AssertWallet(expected.State.Wallet, app.Progression.State.Wallet);
        }

        void PrepareAccount()
        {
            var catalog = CanonicalContent.CreateMetaCatalog();
            var fixture = MetaEngine.NewGame(catalog) with { CompletedRuns = 2, NextRunSequence = 3, NextClaimSequence = 2 };
            foreach (var building in catalog.ManorBuildings) fixture.ManorLevels[building.Id] = building.MaxLevel;
            fixture = fixture with { PendingClaims = new[] { new MetaPendingClaim(1, MetaClaimKind.Idle, "idle", new Dictionary<string, int>(catalog.Economy.IdlePerStep), catalog.Economy.IdleStepSeconds) } };
            SaveFixture(catalog, fixture); ReturningPreference();
            day = 100; var storage = new RewardedAdsAtomicStore(Path.Combine(directory, "policy-fixture"));
            policy = new RewardedAdsPolicy(storage, () => day); day = 101;
            RunCoordinator.EditorRewardPolicyFactory = () => policy = new RewardedAdsPolicy(storage, () => day);
            RunCoordinator.EditorCompletedRuns = 2;
        }
        IEnumerator BackgroundAndReturn(string screen)
        {
            app.EditorCaptureNotifyPause(true); app.EditorCaptureNotifyFocus(false); yield return null;
            app.EditorCaptureNotifyPause(false); app.EditorCaptureNotifyFocus(true); yield return null;
            Assert.That(app.CurrentScreenName, Is.EqualTo(screen)); Assert.That(app.Error, Is.Null);
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
            Assert.That(Named("Title.Enter").targetGraphic.depth, Is.GreaterThanOrEqualTo(0), "The resized title must be registered for rendering before the first pointer input.");
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
                PlayModeWindow.SetCustomRenderingResolution((uint)size.x, (uint)size.y, "Portrait BM fixture QA");
                yield return null; yield return null; app.Ui.SetPortraitMobileSimulation(true); app.Ui.SynchronizeViewport();
                yield return new WaitForSecondsRealtime(.2f); Canvas.ForceUpdateCanvases();
                Assert.That(Screen.width, Is.EqualTo(size.x)); Assert.That(Screen.height, Is.EqualTo(size.y));
                Assert.That(app.CurrentScreenName, Is.EqualTo(expectedScreen));
                var scroll = app.Ui.GetComponentInChildren<ScrollRect>();
                foreach (var position in new[] { 1f, 0f })
                {
                    if (scroll != null) scroll.verticalNormalizedPosition = position;
                    yield return null; yield return null; Canvas.ForceUpdateCanvases(); AssertGeometry();
                    var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-234/editor-bm")); Directory.CreateDirectory(output);
                    var path = Path.Combine(output, name + "-" + size.x + "x" + size.y + (position == 0 ? "-bottom" : "") + ".png");
                    if (File.Exists(path)) File.Delete(path); ScreenCapture.CaptureScreenshot(path); var deadline = Time.realtimeSinceStartup + 10;
                    while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(File.Exists(path), Is.True, path);
                    var image = new Texture2D(2, 2);
                    try { Assert.That(image.LoadImage(File.ReadAllBytes(path)), Is.True); Assert.That(image.width, Is.EqualTo(size.x)); Assert.That(image.height, Is.EqualTo(size.y)); }
                    finally { Object.Destroy(image); }
                    File.WriteAllText(path + ".txt", $"surface=actual Unity Editor PlayMode UI; mobileAspectSimulation=true; physicalDevice=false; userAcceptance=false; automatedPointerActions=true; accountFixture={fixture}; screen={app.CurrentScreenName}; commit={BuildIdentity.Commit}; sourceHash={BuildIdentity.SourceHash}; sourceDirty={BuildIdentity.SourceDirty}; dataHash={CanonicalContent.DataHash}; safePixels={app.Ui.SafePixels}; normalPlayEvidence=false; unconfiguredReleaseAds=unavailable; bmAccountAndRulesFixture=true\n");
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
                else if (button.name.StartsWith("Tab.", StringComparison.Ordinal) || button.name == "Estate.Depart" || button.name == "Departure.Start" || button.name.StartsWith("Summary.", StringComparison.Ordinal) || button.name == "Shop.Back" || button.name == "FakeAd.Back")
                {
                    footer.Add(bounds); Assert.That(bounds.yMax, Is.LessThanOrEqualTo(safe.yMin + safe.height / 3), button.name + " must remain in the lower thumb zone.");
                }
                // The authoritative portrait boards put these dismiss controls above their modals.
                if (button.name == "Pass.Back" || button.name == "Idle.Back")
                    Assert.That(bounds.center.y, Is.GreaterThan(safe.center.y), button.name + " follows the board's upper dismiss position.");
                if (button.name.StartsWith("Revive.", StringComparison.Ordinal) || button.name == "Idle.Claim" || button.name == "Idle.OptionalAd")
                {
                    var panel = Pixels((RectTransform)button.transform.parent);
                    Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(panel.xMin));
                    Assert.That(bounds.xMax, Is.LessThanOrEqualTo(panel.xMax));
                    Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(panel.yMin));
                    Assert.That(bounds.yMax, Is.LessThanOrEqualTo(panel.yMin + panel.height / 3), button.name + " remains in the modal's action band.");
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
