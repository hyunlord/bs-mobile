using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        const string PortraitGlyphs = PortraitRunPanels.Glyphs + " 영지 새봄의 터 집으로 돌아왔습니다 출정 영지로 다시 출정 클리어 쓰러짐 포기 버틴 시간 도달 레벨 종료하시겠어요 종료 취소 끌어서 이동 공격은 자동 익은 밭에 다가가 수확 백성이 싸운 뒤 일터로 복귀";
        PortraitCardActions portraitCardActions;
        double cardActionsUnlockAt;
        CardOfferView guardedOffer;
        int guardedLevel;
        UiScreen exitOrigin;
        Vector2 portraitLayoutSize;
        public string CurrentScreenName => screen.ToString();
        static string PreferencePrefix
        {
            get
            {
                if (WaveBenchmarkAccount.Current != null) return WaveBenchmarkAccount.Current.PreferencesPrefix;
                var automated = AutoplayCapture.Active?.PreferencesPrefix ?? FeelTrialAutomation.Active?.PreferencesPrefix ?? NormalGcProbe.PreferencesPrefix;
                if (automated != null) return automated;
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(PreferencePrefixOverride)) return PreferencePrefixOverride;
#endif
                return RunPreferences.Prefix;
            }
        }
        void ShowPortraitTitle()
        {
            screen = UiScreen.Title; MenuOpen = true; ClearUi(); hud = null;
            PortraitRunPanels.Title(Ui, !preferences.IntroductionSeen,
                () => { if (preferences.IntroductionSeen) ShowPortraitEstate(); else StartRun(); }, () => ShowSettings(false));
        }
        RectTransform PortraitPage(string name, string title)
        {
            var root = UiShell.Rect(name, Ui.Content); UiShell.Stretch(root); Ui.Surface(root, "ui.panel");
            PortraitWidgets.Label(Ui, root, title, 16, 24, Ui.SafeRoot.rect.width - 32, 56, PortraitTokens.Title);
            return root;
        }
        Button PortraitAction(RectTransform root, string name, string label, System.Action action, float bottom)
        {
            var button = PortraitWidgets.Button(Ui, root, label, action, 16, Ui.SafeRoot.rect.height - bottom, Ui.SafeRoot.rect.width - 32, 48);
            button.name = name; return button;
        }
        void ShowPortraitEstate() => ShowAccountEstate();
        public void OpenPause()
        {
            if (!Ui.PortraitEnabled || Session == null || finished) return;
            screen = UiScreen.Pause; MenuOpen = true; Stick.ResetStick(); accumulator = 0; ClearUi(); hud = null;
            PortraitRunPanels.Pause(Ui, CurrentFrame, FirstPlayable, runCatalog ?? FoundationBoot.Catalog, displays,
                ContinuePausedRun, () => ShowSettings(true), () =>
                {
                    screen = UiScreen.ConfirmAbandon; ClearUi();
                    PortraitRunPanels.ConfirmAbandon(Ui, AbandonCurrentRun, OpenPause);
                });
        }
        public void ContinuePausedRun()
        {
            if (Session == null || finished) return;
            discardResumeDelta = true;
            if (CurrentFrame.Status == RunStatus.AwaitingCard) { cardsFrame = null; ShowCards(); }
            else ShowHud();
        }
        public void NavigateBack()
        {
            if (Ui == null || !Ui.PortraitEnabled || loading) return;
            switch (screen)
            {
                case UiScreen.Run: case UiScreen.Cards: OpenPause(); break;
                case UiScreen.Pause: ContinuePausedRun(); break;
                case UiScreen.Settings: RestorePortraitSettings(); break;
                case UiScreen.ConfirmAbandon: OpenPause(); break;
                case UiScreen.Summary: StartCoroutine(ReturnMeta()); break;
                case UiScreen.Title: case UiScreen.Estate: ShowPortraitExit(); break;
                case UiScreen.Departure: ShowPortraitEstate(); break;
                case UiScreen.CardPool: ShowPortraitDeparture(); break;
                case UiScreen.Vassals: RestoreVassalOrigin(); break;
                case UiScreen.Challenges: case UiScreen.Shop: case UiScreen.Idle: ShowPortraitEstate(); break;
                case UiScreen.Pass: case UiScreen.AdRemoval: ShowPortraitShop(); break;
                case UiScreen.Revive: FinishWithoutRevive(); break;
                case UiScreen.FakeAdSettings: RestoreFakeAdSettings(); break;
                case UiScreen.SpendConfirmation: ShowPortraitVassals(); break;
                case UiScreen.ConfirmExit: if (exitOrigin == UiScreen.Title) ShowPortraitTitle(); else ShowPortraitEstate(); break;
            }
        }
        void ShowPortraitExit()
        {
            exitOrigin = screen; screen = UiScreen.ConfirmExit; MenuOpen = true; ClearUi(); hud = null;
            var root = PortraitPage("Portrait.ConfirmExit", "종료하시겠어요?");
            PortraitAction(root, "Exit.Confirm", "종료", Application.Quit, 120);
            PortraitAction(root, "Exit.Cancel", "취소", NavigateBack, 64);
        }
        void RestorePortraitSettings()
        {
            switch (settingsOrigin)
            {
                case UiScreen.Title: ShowPortraitTitle(); break;
                case UiScreen.Estate: ShowPortraitEstate(); break;
                case UiScreen.Summary: ShowSummary(); break;
                case UiScreen.Pause: OpenPause(); break;
                case UiScreen.Departure: ShowPortraitDeparture(); break;
                case UiScreen.Vassals: ShowPortraitVassals(); break;
                case UiScreen.Challenges: ShowPortraitChallenges(); break;
                case UiScreen.Shop: ShowPortraitShop(); break;
                case UiScreen.Idle: ShowPortraitIdle(); break;
                case UiScreen.Pass: ShowPortraitPass(); break;
                case UiScreen.Revive: ShowPortraitRevive(); break;
                default: ContinuePausedRun(); break;
            }
        }
        void ShowPortraitCards(CardOfferView offer)
        {
            var changed = guardedOffer == null || guardedLevel != CurrentFrame.Level ||
                guardedOffer.Rerolls != offer.Rerolls || guardedOffer.Bans != offer.Bans ||
                guardedOffer.Cards.Count != offer.Cards.Count;
            if (!changed)
                for (var i = 0; i < offer.Cards.Count; i++)
                    if (guardedOffer.Cards[i] != offer.Cards[i]) { changed = true; break; }
            if (changed) cardActionsUnlockAt = Time.unscaledTimeAsDouble + .3;
            NotifyRewardLevel();
            guardedOffer = offer; guardedLevel = CurrentFrame.Level;
            portraitCardActions = PortraitRunPanels.Cards(Ui, CurrentFrame, FirstPlayable, offer,
                runCatalog ?? FoundationBoot.Catalog, displays, (kind, id) =>
                {
                    if (screen == UiScreen.Cards && Time.unscaledTimeAsDouble >= cardActionsUnlockAt)
                        Send(kind, card: id);
                }, RewardOffer(RewardedAdSlot.Reroll), () =>
                { if (Time.unscaledTimeAsDouble >= cardActionsUnlockAt) RequestReward(RewardedAdSlot.Reroll); });
            portraitCardActions.SetActionsEnabled(Time.unscaledTimeAsDouble >= cardActionsUnlockAt);
        }
        void RefreshPortraitCardGate()
        {
            if (screen == UiScreen.Cards && portraitCardActions != null && !portraitCardActions.Enabled && Time.unscaledTimeAsDouble >= cardActionsUnlockAt)
                portraitCardActions.SetActionsEnabled(true);
        }
        void UpdatePortraitNavigation()
        {
            if (!Ui.PortraitEnabled) return;
            var size = Ui.SafeRoot.rect.size;
            if (size != portraitLayoutSize)
            {
                var initialized = portraitLayoutSize != Vector2.zero; portraitLayoutSize = size;
                if (initialized && !loading)
                {
                    var scroll = Ui.Content.GetComponentInChildren<ScrollRect>();
                    var position = scroll != null ? scroll.verticalNormalizedPosition : 1;
                    switch (screen)
                    {
                        case UiScreen.Title: ShowPortraitTitle(); break;
                        case UiScreen.Estate: ShowPortraitEstate(); break;
                        case UiScreen.Pause: OpenPause(); break;
                        case UiScreen.Settings: ShowSettings(Session != null && !finished); break;
                        case UiScreen.Summary: ShowSummary(); break;
                        case UiScreen.Cards: cardsFrame = null; ShowCards(); break;
                        case UiScreen.Departure: ShowPortraitDeparture(); break;
                        case UiScreen.Vassals: ShowPortraitVassals(); break;
                        case UiScreen.Challenges: ShowPortraitChallenges(); break;
                        case UiScreen.CardPool: ShowPortraitCardPool(); break;
                        case UiScreen.SpendConfirmation: ShowPortraitSpend(); break;
                        case UiScreen.Shop: ShowPortraitShop(); break;
                        case UiScreen.Idle: ShowPortraitIdle(); break;
                        case UiScreen.Pass: ShowPortraitPass(); break;
                        case UiScreen.Revive: ShowPortraitRevive(); break;
                        case UiScreen.AdRemoval: ShowPortraitAdRemoval(); break;
                        case UiScreen.FakeAdSettings: ShowFakeAdSettings(); break;
                        case UiScreen.ConfirmAbandon:
                            ClearUi(); PortraitRunPanels.ConfirmAbandon(Ui, AbandonCurrentRun, OpenPause); break;
                        case UiScreen.ConfirmExit:
                            var origin = exitOrigin; ShowPortraitExit(); exitOrigin = origin; break;
                    }
                    scroll = Ui.Content.GetComponentInChildren<ScrollRect>();
                    if (scroll != null) scroll.verticalNormalizedPosition = position;
                }
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) NavigateBack();
        }
        void PausePortraitForLifecycle()
        {
            if (Ui != null && Ui.PortraitEnabled && !waveBenchmarkRequested && !visualReviewRequested && AutoplayCapture.Active == null
                && (screen == UiScreen.Run || screen == UiScreen.Cards)) OpenPause();
        }
        void ShowPortraitSummary() => ShowAccountResult();
        float PortraitHintLeft()
        {
            var count = FirstPlayable?.Charters.Count ?? 0;
            if (CurrentFrame != null)
                for (var i = 0; i < CurrentFrame.Equipment.Count; i++)
                {
                    var id = CurrentFrame.Equipment[i].Id;
                    if (runCatalog.Weapons.ContainsKey(id) || runCatalog.Tools.ContainsKey(id)) count++;
                }
            return 20 + count * 27.5f;
        }
        bool PortraitHintFits => Ui.SafeRoot.rect.width - PortraitHintLeft() - 12 >= 170;
        void LayoutPortraitHint()
        {
            var rect = (RectTransform)activeHint.transform.parent;
            rect.SetParent(Ui.Content, false); rect.gameObject.SetActive(true);
            var left = PortraitHintLeft();
            PortraitWidgets.Position(rect, left, 44, Ui.SafeRoot.rect.width - left - 12, 32);
            activeHint.text = activeHintKind == FirstRunHint.Move ? "끌어서 이동 · 공격은 자동" : activeHintKind == FirstRunHint.Harvest ? "익은 밭에 다가가 수확" : "백성이 싸운 뒤 일터로 복귀";
            activeHint.fontSize = PortraitTokens.Small; UiShell.Stretch(activeHint.rectTransform, 6);
            var image = rect.GetComponent<Image>(); if (image != null) { image.raycastTarget = false; image.pixelsPerUnitMultiplier = UiTokens.FallowFrameMultiplier; }
            activeHint.raycastTarget = false;
        }
    }
}
