using System;
using System.Collections.Generic;
using System.Linq;
using Game.View;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        const string PortraitMetaGlyphs = "출정 준비 가신 도전 상점 방치 생산 곧 찾아옵니다 돌아가기 출정할 수 없는 지역입니다 레벨업 승급 비용 조각 확인 취소 저장하지 못했습니다 다시 시도해 주세요 가신 전투 시제품 개발 다음 출정부터 다음 출정에 적용 출정 시 편성 고정 합산 상한 도달 공격 체력 이동 생산 아군 경험치 ‰ →";
        readonly Dictionary<string, int> resultFragments = new Dictionary<string, int>();
        string selectedVassal, spendVassal;
        bool spendRank;
        UiScreen vassalOrigin = UiScreen.Estate;
        MetaRunFacts AccountFacts(bool quit)
        {
            var summary = completedSummary ?? Session.GetSummary();
            return IsWave ? MetaRunAdapter.FromWave(summary, CurrentFrame, CurrentWave, quit)
                : MetaRunAdapter.FromInteractive(summary, CurrentFrame, FirstPlayable, quit);
        }
        MetaSettlement SettleAccountRun(bool quit)
        {
            var before = Progression.State;
            var result = Progression.SettleRun(AccountFacts(quit));
            resultFragments.Clear();
            foreach (var pair in result.State.Vassals)
            {
                var delta = pair.Value.Fragments - before.Vassals[pair.Key].Fragments;
                if (delta > 0) resultFragments.Add(pair.Key, delta);
            }
            return result;
        }
        public void NavigatePortraitMeta(PortraitMetaPage page)
        {
            if (Ui == null || !Ui.PortraitEnabled || loading || Progression.State.PendingRun != null) return;
            switch (page)
            {
                case PortraitMetaPage.Estate: ShowPortraitEstate(); break;
                case PortraitMetaPage.Departure: ShowPortraitDeparture(); break;
                case PortraitMetaPage.Vassals: vassalOrigin = screen == UiScreen.Departure ? screen : UiScreen.Estate; ShowPortraitVassals(); break;
                case PortraitMetaPage.Challenges: ShowPortraitChallenges(); break;
                case PortraitMetaPage.Shop: ShowPortraitShop(); break;
                case PortraitMetaPage.Idle: ShowPortraitIdle(); break;
            }
        }
        void ShowAccountEstate()
        {
            try
            {
                recentIdle = Progression.AdvanceIdle();
                MetaPage(UiScreen.Estate, () => PortraitMetaPanels.Estate(Ui, Progression.Catalog, Progression.State,
                    id => MetaAction(() => Progression.SetManorPriority(id), ShowPortraitEstate),
                    ShowPortraitDeparture, () => ShowSettings(false), NavigatePortraitMeta,
                    sequence => MetaAction(() => Progression.Claim(sequence), ShowPortraitEstate)));
            }
            catch (Exception e) { Fail(e); }
        }
        bool CanDepart(string chapterId)
        {
            var chapter = Progression.Catalog.Chapters.FirstOrDefault(c => c.Id == chapterId);
            return chapter != null && chapter.Id == FoundationBoot.Catalog.WaveRuntime.ChapterId
                && chapter.Index <= Progression.State.HighestClearedChapter + 1 && Progression.State.PendingRun == null;
        }
        void ShowPortraitDeparture()
        {
            MetaPage(UiScreen.Departure, () => PortraitMetaPanels.Departure(Ui, Progression.Catalog, Progression.State,
                FoundationBoot.Catalog, displays, selectedChapter,
                id => { selectedChapter = id; ShowPortraitDeparture(); },
                () => { if (CanDepart(selectedChapter)) StartRun(); }, ShowPortraitCardPool,
                () => { vassalOrigin = UiScreen.Departure; ShowPortraitVassals(); }, ShowPortraitEstate));
        }
        void ShowPortraitCardPool() => MetaPage(UiScreen.CardPool,
            () => PortraitMetaPanels.CardPool(Ui, FoundationBoot.Catalog, displays, ShowPortraitDeparture));
        void ShowPortraitVassals()
        {
            if (selectedVassal == null) selectedVassal = Progression.Catalog.Vassals[0].Id;
            MetaPage(UiScreen.Vassals, () => PortraitMetaPanels.Vassals(Ui, Progression.Catalog, Progression.State, selectedVassal,
                id => { selectedVassal = id; ShowPortraitVassals(); },
                id => RequestPortraitSpend(id, false), id => RequestPortraitSpend(id, true),
                id => MetaAction(() => Progression.ToggleVassal(id), ShowPortraitVassals), NavigatePortraitMeta, DepartureVassals(true)));
        }
        void RestoreVassalOrigin()
        { if (vassalOrigin == UiScreen.Departure) ShowPortraitDeparture(); else ShowPortraitEstate(); }
        void ShowPortraitChallenges() => MetaPage(UiScreen.Challenges,
            () => PortraitMetaPanels.Challenges(Ui, Progression.Catalog, Progression.State,
                sequence => MetaAction(() => Progression.Claim(sequence), ShowPortraitChallenges), NavigatePortraitMeta, displays));
        void RequestPortraitSpend(string id, bool rank)
        { spendVassal = id; spendRank = rank; ShowPortraitSpend(); }
        void ShowPortraitSpend()
        {
            var state = Progression.State; var catalog = Progression.Catalog;
            var definition = catalog.Vassals.Single(v => v.Id == spendVassal); var value = state.Vassals[spendVassal];
            string detail; bool affordable;
            if (spendRank)
            {
                int cost = value.Rank < definition.MaxRank ? definition.RankCosts[value.Rank] : 0;
                detail = definition.Name + " · 조각 " + cost;
                affordable = value.Unlocked && value.Rank < definition.MaxRank && value.Fragments >= cost;
            }
            else
            {
                var costs = definition.LevelCostBase.Keys.Union(definition.LevelCostStep.Keys).ToDictionary(k => k,
                    k => (definition.LevelCostBase.TryGetValue(k, out var b) ? b : 0) +
                         (definition.LevelCostStep.TryGetValue(k, out var step) ? step : 0) * (value.Level - 1));
                int forge = catalog.ManorBuildings.Where(b => b.Effect == ManorEffect.VassalLevelCap).Sum(b => state.ManorLevels[b.Id]);
                int cap = Math.Min(definition.MaxLevel, catalog.Economy.BaseVassalLevelCap + forge * catalog.Economy.LevelCapPerForgeLevel);
                detail = definition.Name + " · " + MetaScreens.Amounts(catalog, costs);
                affordable = value.Unlocked && value.Level < cap && costs.All(x => state.Wallet[x.Key] >= x.Value);
            }
            MetaPage(UiScreen.SpendConfirmation, () => PortraitMetaPanels.SpendConfirmation(Ui,
                spendRank ? "승급" : "레벨업", detail,
                () => MetaAction(() => { if (spendRank) Progression.RankUpVassal(spendVassal); else Progression.UpgradeVassal(spendVassal); }, ShowPortraitVassals),
                ShowPortraitVassals, affordable));
        }
        void ShowAccountResult()
        {
            PortraitMetaPanels.Result(Ui, Progression.Catalog, selectedChapter, AccountFacts(abandoned), settlement,
                CurrentFrame.TickRate, CurrentFrame.Level, resultFragments,
                () => StartCoroutine(ReturnPortraitDeparture()), () => StartCoroutine(ReturnMeta()),
                RewardOffer(RewardedAdSlot.Result), () => RequestReward(RewardedAdSlot.Result), Session?.HostRevived == true);
        }
        System.Collections.IEnumerator ReturnPortraitDeparture()
        { yield return ReturnMeta(); ShowPortraitDeparture(); }
    }
}
