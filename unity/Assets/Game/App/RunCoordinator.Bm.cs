using System;
using System.Globalization;
using System.Linq;
using Game.View;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        const string BmGlyphs = "광고 보고 2배 바로 2배 광고로 일어나기 바로 일어나기 광고 다시 뽑기 +1 바로 다시 뽑기 +1 오늘 부활한 출정 현재 외형 코발트 망토 현재 영지 휴경의 왕국 고정 보상 미정 보기 전용 실제 구매는 제공하지 않습니다 개발 광고 시험 보상 저장 다시 시도";
        RewardedAdsPolicy rewardPolicy;
        IRewardedAdProvider rewardProvider;
        HostRewardRunOptions nextHostRewards, activeHostRewards;
        MetaBonusPolicy resultBonus;
        bool adRemovalOwned, reviveDeclined;
        string rewardRunId = "";
        long rewardRunSequence;
        int rewardRunCompletedCount, rewardLevel;
#if UNITY_EDITOR
        public static Func<RewardedAdsPolicy> EditorRewardPolicyFactory { get; set; }
        public static HostRewardRunOptions EditorHostRewardOptions { get; set; }
        public static IRewardedAdProvider EditorRewardProvider { get; set; }
        public static MetaBonusPolicy EditorResultBonus { get; set; }
        public static bool EditorAdRemovalOwned { get; set; }
        public static int? EditorCompletedRuns { get; set; }
        public void EditorPresentSettledRewardFixture(MetaSettlement fixture)
        {
            if(!finished || Progression.State.PendingRun != null || Progression.State.ResultRewardBasis == null)
                throw new InvalidOperationException("Fixture requires a closed session and a persisted settled reward basis.");
            settlement = fixture;
            rewardRunSequence = Progression.State.ResultRewardBasis.RunSequence;
            rewardRunId = "run:" + rewardRunSequence.ToString(CultureInfo.InvariantCulture);
            rewardRunCompletedCount = CompletedRewardRuns;
            ShowSummary();
        }
        public MetaSettlement EditorRewardSettlement => settlement;
        public void EditorRetryRewardDelivery() { rewardPolicy.FlushPendingWrite(); DeliverPendingRewards(); ShowSummary(); }
        public void EditorAdvanceRewardFixture(int maxCommands)
        {
            for(int i = 0; i < maxCommands && Session != null && Session.View.Status != RunStatus.Completed && Error == null; i++)
            {
                if(Session.View.Status == RunStatus.AwaitingCard)
                {
                    var offer = Session.View.CaptureCards();
                    Send(ReplayCommandKind.ChooseCard, card: offer.Cards[0]);
                }
                else Send(ReplayCommandKind.Advance);
            }
            if(Session?.View.Status == RunStatus.Completed && !finished) CompleteRun();
        }
#endif
        void InitializeRewardedAds()
        {
            rewardPolicy = new RewardedAdsPolicy(new RewardedAdsAtomicStore(MetaDirectory),
                () => int.Parse(DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            rewardProvider = new RewardedAdsFakeProvider();
#else
            rewardProvider = new RewardedAdsUnavailableProvider();
#endif
#if UNITY_EDITOR
            if(EditorRewardPolicyFactory != null) rewardPolicy = EditorRewardPolicyFactory();
            if(EditorRewardProvider != null) rewardProvider = EditorRewardProvider;
            nextHostRewards = EditorHostRewardOptions;
            resultBonus = EditorResultBonus;
            adRemovalOwned = EditorAdRemovalOwned;
#endif
            // Closed sessions cannot receive run commands. Preserve their consumed quota without applying to a new run.
            foreach(var intent in rewardPolicy.PendingRewards)
                if(intent.Slot == RewardedAdSlot.Revive || intent.Slot == RewardedAdSlot.Reroll)
                    rewardPolicy.ExpireClosedRunReward(intent.CompletionId, intent.RunId);
            rewardRunSequence = Progression.State.ResultRewardBasis?.RunSequence ?? 0;
            rewardRunId = rewardRunSequence > 0 ? "run:" + rewardRunSequence.ToString(CultureInfo.InvariantCulture) : "";
            DeliverPendingRewards();
        }
        HostRewardRunOptions DepartureHostRewards(bool accountRun)
            => activeHostRewards = IsWave && accountRun && !waveBenchmarkRequested && !visualReviewRequested && AutoplayCapture.Active == null
                ? nextHostRewards ?? new HostRewardRunOptions(true, null) : null;
        int CompletedRewardRuns
        {
            get
            {
#if UNITY_EDITOR
                if(EditorCompletedRuns.HasValue) return EditorCompletedRuns.Value;
#endif
                return (int)Math.Min(int.MaxValue, Progression.State.CompletedRuns);
            }
        }
        void BeginRewardedRun()
        {
            rewardRunSequence = Progression.State.PendingRun?.Sequence ?? 0;
            rewardRunId = rewardRunSequence > 0 ? "run:" + rewardRunSequence.ToString(CultureInfo.InvariantCulture) : recording.SessionId;
            rewardRunCompletedCount = CompletedRewardRuns;
            reviveDeclined = false; rewardLevel = CurrentFrame.Level;
            rewardPolicy.NotifyMeaningfulGameplay(rewardRunId, 0);
            DeliverPendingRewards();
        }
        void NotifyRewardLevel()
        {
            if(CurrentFrame.Level <= rewardLevel || string.IsNullOrEmpty(rewardRunId)) return;
            rewardLevel = CurrentFrame.Level;
            rewardPolicy.NotifyMeaningfulGameplay(rewardRunId, rewardLevel);
        }
        MetaPendingClaim IdleRewardClaim() => Progression.State.PendingClaims.FirstOrDefault(c => c.Kind == MetaClaimKind.Idle);
        RewardedAdContext RewardContext(RewardedAdSlot slot)
        {
            var idle = slot == RewardedAdSlot.Idle ? IdleRewardClaim() : null;
            var offer = slot == RewardedAdSlot.Reroll && Session != null ? Session.View.CaptureCards() : null;
            bool resultAvailable = slot == RewardedAdSlot.Result && settlement != null &&
                Progression.State.ResultRewardBasis?.RunSequence == rewardRunSequence && settlement.Awarded.Values.Any(v => v > 0);
            return new RewardedAdContext(slot, rewardRunId, idle == null ? "" : idle.Sequence.ToString(CultureInfo.InvariantCulture),
                slot == RewardedAdSlot.Idle ? CompletedRewardRuns : rewardRunCompletedCount, adRemovalOwned,
                Session?.View.Status == RunStatus.AwaitingCard && activeHostRewards?.AllowRerollGrant == true,
                offer?.Rerolls ?? 0, Session?.CanHostRevive == true && !finished && !reviveDeclined,
                slot == RewardedAdSlot.Idle ? idle != null && idle.Rewards.Values.Any(v => v > 0) : resultAvailable,
                resultBonus != null, activeHostRewards?.Revive != null);
        }
        BmAdOffer RewardOffer(RewardedAdSlot slot)
        {
            if(rewardPolicy == null) return null;
            var offer = rewardPolicy.Evaluate(RewardContext(slot), rewardProvider.IsAvailable);
            string label = slot == RewardedAdSlot.Revive ? (offer.DirectReward ? "바로 일어나기" : "광고로 일어나기") :
                slot == RewardedAdSlot.Reroll ? (offer.DirectReward ? "바로 다시 뽑기 +1" : "광고 다시 뽑기 +1") :
                offer.DirectReward ? "바로 2배" : "광고 보고 2배";
            if(slot == RewardedAdSlot.Idle) label += " · 오늘 " + rewardPolicy.TodayIdleCompletions + "/3";
            return new BmAdOffer(offer.Visible, offer.Visible, label);
        }
        void RequestReward(RewardedAdSlot slot)
        {
            try
            {
                var context = RewardContext(slot);
                rewardPolicy.Request(context, Guid.NewGuid().ToString("N"), rewardProvider);
                rewardPolicy.FlushPendingWrite(); DeliverPendingRewards();
                switch(slot)
                {
                    case RewardedAdSlot.Reroll: cardsFrame = null; ShowCards(); break;
                    case RewardedAdSlot.Revive:
                        if(Session.HostRevived) { completedSummary = null; reviveDeclined = false; if(CurrentFrame.Status == RunStatus.AwaitingCard) { cardsFrame = null; ShowCards(); } else ShowHud(); }
                        else ShowPortraitRevive();
                        break;
                    case RewardedAdSlot.Result: ShowSummary(); break;
                    case RewardedAdSlot.Idle: ShowPortraitIdle(); break;
                }
            }
            catch(Exception error) { Fail(error); }
        }
        void DeliverPendingRewards()
        {
            foreach(var intent in rewardPolicy.PendingRewards)
            {
                bool delivered = false;
                switch(intent.Slot)
                {
                    case RewardedAdSlot.Reroll:
                    case RewardedAdSlot.Revive:
                        if(intent.RunId != rewardRunId || Session == null || recording == null || finished)
                        { rewardPolicy.ExpireClosedRunReward(intent.CompletionId, intent.RunId); continue; }
                        if(intent.Slot == RewardedAdSlot.Reroll)
                        {
                            if(Session.HostRerollsGranted < intent.Ordinal)
                            {
                                if(Session.HostRerollsGranted + 1 != intent.Ordinal) continue;
                                Send(ReplayCommandKind.HostGrantRerollV1, value: 1);
                            }
                            delivered = Error == null && Session.HostRerollsGranted >= intent.Ordinal;
                        }
                        else
                        {
                            if(!Session.HostRevived) Send(ReplayCommandKind.HostReviveV1);
                            delivered = Error == null && Session.HostRevived;
                        }
                        if(delivered) recording.Checkpoint(Session);
                        break;
                    case RewardedAdSlot.Idle:
                        if(!long.TryParse(intent.ClaimId, out var claimSequence)) throw new InvalidOperationException("Invalid idle receipt identity.");
                        var idleResult = Progression.ClaimIdleWithBonus(claimSequence, intent.CompletionId,
                            new MetaBonusPolicy(1000, Progression.Catalog.Materials.ToDictionary(m => m.Id, m => m.WalletCap)));
                        delivered = idleResult.State.RewardReceipts.Any(r => r.Kind == MetaBonusKind.Idle && r.SourceSequence == claimSequence); break;
                    case RewardedAdSlot.Result:
                        if(resultBonus == null || !intent.RunId.StartsWith("run:", StringComparison.Ordinal) ||
                            !long.TryParse(intent.RunId.Substring(4), out var resultSequence)) continue;
                        var bonusResult = Progression.GrantResultBonus(resultSequence, intent.CompletionId, resultBonus);
                        delivered = bonusResult.State.RewardReceipts.Any(r => r.Kind == MetaBonusKind.Result && r.SourceSequence == resultSequence);
                        if(bonusResult.Granted && settlement != null && resultSequence == rewardRunSequence)
                        {
                            var total = settlement.Awarded.ToDictionary(p => p.Key, p => p.Value);
                            foreach(var amount in bonusResult.BonusAwarded) total[amount.Key] = checked(total[amount.Key] + amount.Value);
                            var overflow = settlement.Overflow.ToDictionary(p => p.Key, p => p.Value);
                            foreach(var amount in bonusResult.Overflow)
                            {
                                overflow.TryGetValue(amount.Key, out var previousOverflow);
                                overflow[amount.Key] = (int)Math.Min(int.MaxValue, (long)previousOverflow + amount.Value);
                            }
                            settlement = settlement with { State = bonusResult.State, Awarded = total, Overflow = overflow,
                                BuildingsGrown = settlement.BuildingsGrown.Concat(bonusResult.BuildingsGrown).Distinct(StringComparer.Ordinal).ToArray() };
                        }
                        break;
                }
                if(delivered) rewardPolicy.Acknowledge(intent.CompletionId);
            }
        }
        bool TryDeferForRevive()
        {
            if(screen == UiScreen.Revive) return true;
            if(!Ui.PortraitEnabled || reviveDeclined || finished || Session?.CanHostRevive != true || RewardOffer(RewardedAdSlot.Revive)?.Visible != true) return false;
            ShowPortraitRevive(); return true;
        }
        void FinishWithoutRevive() { reviveDeclined = true; screen = UiScreen.Run; RetryCompleteRun(); }
        void ShowPortraitRevive() => MetaPage(UiScreen.Revive, () =>
        {
            var pending = rewardPolicy.HasPendingWrite || rewardPolicy.PendingRewards.Any(intent => intent.Slot == RewardedAdSlot.Revive && intent.RunId == rewardRunId);
            Action retrySave = pending ? () => RequestReward(RewardedAdSlot.Revive) : (Action)null;
            PortraitBmPanels.Revive(Ui, new BmReviveView(RewardOffer(RewardedAdSlot.Revive)), FinishWithoutRevive,
                () => RequestReward(RewardedAdSlot.Revive), retrySave);
        });
        void ShowPortraitShop() => MetaPage(UiScreen.Shop, () => PortraitBmPanels.Shop(Ui,
            new BmShopView(new[] { "현재 외형 · 코발트 망토" }, new[] { "현재 영지 · 휴경의 왕국" }, adRemovalOwned,
                new[] { "hero.core.frontier_knight.idle" }, new[] { Progression.Catalog.ManorBuildings[0].ArtRole }),
            ShowPortraitPass, ShowPortraitAdRemoval, ShowPortraitEstate));
        void ShowPortraitPass() => MetaPage(UiScreen.Pass, () => PortraitBmPanels.Pass(Ui,
            new BmPassView(new[] { "고정 보상 [n]" }, new[] { "고정 보상 [n]" }), ShowPortraitShop));
        void ShowPortraitAdRemoval() => MetaPage(UiScreen.AdRemoval, () =>
        {
            var root = PortraitPage("Portrait.AdRemoval", "광고 제거");
            PortraitWidgets.Label(Ui, root, "가격 [n] · 보기 전용\n실제 구매는 제공하지 않습니다.", 18, 100, Ui.SafeRoot.rect.width - 36, 100);
            PortraitAction(root, "AdRemoval.Back", "돌아가기", ShowPortraitShop, 64);
        });
        void ShowPortraitIdle()
        {
            Progression.AdvanceIdle(); DeliverPendingRewards();
            var state = Progression.State; var catalog = Progression.Catalog; var claim = IdleRewardClaim();
            long levels = catalog.ManorBuildings.Where(b => b.Effect == ManorEffect.IdleCapacity).Sum(b => state.ManorLevels[b.Id]);
            long capacity = Math.Min(catalog.Economy.MaximumIdleCapSeconds, catalog.Economy.BaseIdleCapSeconds + levels * catalog.Economy.IdleCapSecondsPerLevel);
            MetaPage(UiScreen.Idle, () => PortraitBmPanels.Idle(Ui,
                new BmIdleView((claim?.IdleSeconds ?? 0) + state.IdleRemainderSeconds, capacity,
                    claim == null ? "" : MetaScreens.Amounts(catalog, claim.Rewards), claim != null && claim.Rewards.Values.Any(v => v > 0),
                    RewardOffer(RewardedAdSlot.Idle), catalog.Materials[0].ArtRole),
                () => { if(claim != null) MetaAction(() => Progression.Claim(claim.Sequence), ShowPortraitIdle); },
                () => RequestReward(RewardedAdSlot.Idle), ShowPortraitEstate));
        }
        Action FakeAdSettingsAction
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return rewardProvider is RewardedAdsFakeProvider ? ShowFakeAdSettings : (Action)null;
#else
                return null;
#endif
            }
        }
        void RestoreFakeAdSettings() { screen = UiScreen.Settings; ShowSettings(Session != null && !finished); }
        void ShowFakeAdSettings()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var provider = (RewardedAdsFakeProvider)rewardProvider;
            MetaPage(UiScreen.FakeAdSettings, () => PortraitBmPanels.FakeSettings(Ui,
                provider.Succeed ? BmFakeOutcome.Success : BmFakeOutcome.Failure,
                outcome => { provider.Succeed = outcome == BmFakeOutcome.Success; ShowFakeAdSettings(); },
                RestoreFakeAdSettings));
#endif
        }
    }
}
